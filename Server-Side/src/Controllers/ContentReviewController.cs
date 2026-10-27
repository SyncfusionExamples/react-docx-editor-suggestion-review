using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.Drawing;
using WDocument = Syncfusion.DocIO.DLS.WordDocument;

namespace SyncfusionDocument.Controllers
{
    // ---- POC AI Content Review workflow ----
    // AI-generated content in the DOCX is written in red text. During import we
    // scan for red text with DocIO, wrap each segment in a bookmark (AI_1, AI_2,
    // ...), and generate a suggestions JSON that is stored separately from the
    // document. The React client loads the document into the Document Editor
    // and then requests the suggestions JSON to build the review panel.
    //
    // A single document is used throughout: App_Data/ContentReview/source.docx.
    // It is bookmarked in place during Initialize, and every review action and
    // Save writes back to the same file.

    public class AiSuggestion
    {
        public int Id { get; set; }
        public string BookmarkName { get; set; }
        public string SuggestionText { get; set; }
        public string ParagraphBookmark { get; set; }
        public string Status { get; set; } // Pending | Approved | Rejected
    }

    public class ContentReviewPayload
    {
        public List<AiSuggestion> Suggestions { get; set; }
    }

    public class ReviewActionParameter
    {
        public string Content { get; set; } // SFDT from the editor (on save/export)
        public List<AiSuggestion> Suggestions { get; set; }
        public string BookmarkName { get; set; }
        public string FileName { get; set; }
    }

    [Route("api/[controller]")]
    public class ContentReviewController : Controller
    {
        private readonly string _dataFolder;

        public ContentReviewController(IWebHostEnvironment hostingEnvironment)
        {
            _dataFolder = Path.Combine(hostingEnvironment.ContentRootPath, "App_Data", "ContentReview");
            Directory.CreateDirectory(_dataFolder);
        }

        // The single document used for the whole review flow.
        private string DocumentPath => Path.Combine(_dataFolder, "source.docx");
        private string SuggestionsPath => Path.Combine(_dataFolder, "suggestions.json");

        // ============ Import / initialization ============

        // Scans the uploaded document for red AI text, inserts AI_n / Para_n
        // bookmarks in place, persists it as the review document, and prepares
        // the suggestions JSON. Called by the editor's Open button (via Import
        // in DocumentEditorController) and by LoadDocument below.
        // Returns the SFDT of the bookmarked document so the editor receives
        // the bookmarks needed for suggestion navigation.
        internal string ImportAndScan(Stream stream)
        {
            var doc = new WDocument(stream, FormatType.Docx);

            try
            {
                var suggestions = ScanAndBookmarkRedText(doc);

                PersistSuggestions(suggestions);

                using (var sfdtStream = new MemoryStream())
                {
                    doc.Save(sfdtStream, FormatType.Docx);
                    sfdtStream.Position = 0;

                    var document =
                        Syncfusion.EJ2.DocumentEditor.WordDocument.Load(
                            sfdtStream,
                            Syncfusion.EJ2.DocumentEditor.FormatType.Docx);

                    try
                    {
                        return Newtonsoft.Json.JsonConvert.SerializeObject(document);
                    }
                    finally
                    {
                        document.Dispose();
                    }
                }
            }
            finally
            {
                doc.Close();
            }
        }

        // Clears the suggestions JSON (used when a New/empty document is opened
        // in the editor, so the review panel resets to 0 suggestions).
        [HttpPost]
        [EnableCors("AllowAllOrigins")]
        [Route("ResetSuggestions")]
        public IActionResult ResetSuggestions()
        {
            // Writes an empty JSON (not deleting the file): a present-but-empty
            // JSON is a deliberate reset and must NOT be rebuilt from bookmarks.
            PersistSuggestions(new List<AiSuggestion>());
            return Json(new { result = "reset" });
        }

        // Returns the document as SFDT for the Document Editor. A predefined
        // document in App_Data may not have been scanned yet (no AI bookmarks
        // and no suggestions JSON): in that case the same scan + bookmark +
        // persist flow used by Import runs here first, so both paths behave
        // identically.
        [HttpPost]
        [EnableCors("AllowAllOrigins")]
        [Route("LoadDocument")]
        public string LoadDocument()
        {
            byte[] bytes = System.IO.File.ReadAllBytes(DocumentPath);

            using (var stream = new MemoryStream(bytes))
            {
                return ImportAndScan(stream);
            }
        }

        // Returns the stored suggestions JSON for the review panel. If the
        // stored JSON is missing/empty but the document carries AI bookmarks
        // (e.g. first load after deployment, or JSON got reset), it is rebuilt
        // from the bookmarks so the pre-loaded document shows its suggestions.
        [HttpGet]
        [EnableCors("AllowAllOrigins")]
        [Route("GetSuggestions")]
        public IActionResult GetSuggestions()
        {
            var suggestions = LoadSuggestions();
            // Rebuild from the document's AI bookmarks only when the stored JSON
            // is missing entirely (e.g. first run after deployment). A present
            // but empty JSON is a deliberate reset (New document), so it stays 0.
            if (suggestions == null && System.IO.File.Exists(DocumentPath))
            {
                suggestions = RebuildSuggestionsFromBookmarks();
                PersistSuggestions(suggestions);
            }
            return Content(Newtonsoft.Json.JsonConvert.SerializeObject(new ContentReviewPayload { Suggestions = suggestions }), "application/json");
        }

        // Recovers the suggestions from the AI_ bookmarks already present in
        // the review document (text = bookmarked runs, paragraph from the
        // owning paragraph's Para_ bookmark or index).
        private List<AiSuggestion> RebuildSuggestionsFromBookmarks()
        {
            var suggestions = new List<AiSuggestion>();
            if (!System.IO.File.Exists(DocumentPath))
                return suggestions;

            var doc = new WDocument(DocumentPath, FormatType.Docx);
            try
            {
                int paraIndex = 0;
                foreach (WSection section in doc.Sections)
                {
                    foreach (var entity in section.Body.ChildEntities)
                    {
                        if (entity is not WParagraph paragraph)
                            continue;
                        paraIndex++;

                        foreach (var child in paragraph.ChildEntities)
                        {
                            if (child is not BookmarkStart bs || !bs.Name.StartsWith("_AI_"))
                                continue;
                            // Text between this BookmarkStart and its BookmarkEnd.
                            string text = "";
                            var node = bs.NextSibling;
                            int guard = 0;
                            while (node != null && !(node is BookmarkEnd be && be.Name == bs.Name) && guard++ < 1000)
                            {
                                if (node is WTextRange range)
                                    text += range.Text;
                                node = node.NextSibling;
                            }
                            if (text.Length == 0)
                                continue;

                            int id = 0;
                            int.TryParse(bs.Name.Substring(4), out id);
                            suggestions.Add(new AiSuggestion
                            {
                                Id = id,
                                BookmarkName = bs.Name,
                                SuggestionText = text,
                                ParagraphBookmark = "_Para_" + paraIndex,
                                Status = "Pending"
                            });
                        }
                    }
                }
            }
            finally
            {
                doc.Close();
            }
            suggestions.Sort((a, b) => a.Id.CompareTo(b.Id));
            return suggestions;
        }

        // ============ Suggestion extraction (DocIO) ============

        // True when the document already carries AI_ bookmarks from a previous
        // import (used to keep Initialize idempotent with a single document).
        private static bool HasReviewBookmarks(WDocument doc)
        {
            for (int i = 0; i < doc.Bookmarks.Count; i++)
                if (IsReviewBookmark(doc.Bookmarks[i].Name))
                    return true;
            return false;
        }

        // Walks every paragraph (body, tables, nested tables), finds red text
        // segments, merges consecutive red runs, and inserts a bookmark around
        // each segment. Also records a paragraph id (Para_n) per paragraph.
        private static List<AiSuggestion> ScanAndBookmarkRedText(WDocument doc)
        {
            var suggestions = new List<AiSuggestion>();
            int aiIndex = 0, paraIndex = 0;

            foreach (WSection section in doc.Sections)
            {
                foreach (var paragraph in EnumerateParagraphs(section))
                {
                    paraIndex++;
                    string paraBookmark = "_Para_" + paraIndex;

                    // Collect text runs: (child index, cumulative offset, text, isRed)
                    var runs = new List<(int Index, int Offset, string Text, bool Red)>();
                    int pos = 0;
                    for (int i = 0; i < paragraph.ChildEntities.Count; i++)
                    {
                        if (paragraph.ChildEntities[i] is WTextRange run)
                        {
                            var c = run.CharacterFormat.TextColor;
                            runs.Add((i, pos, run.Text, c.R >= 180 && c.G <= 80 && c.B <= 80));
                            pos += run.Text.Length;
                        }
                    }

                    // Merge consecutive red runs into segments. Bookmark names
                    // are assigned in document order (first segment = lowest
                    // number), but inserted LAST-to-FIRST so an insertion never
                    // shifts the child-entity indices of segments still to be
                    // processed (inserting earlier moves later runs by one).
                    var segments = new List<(int First, int Last, string Text, string Name)>();
                    int r = 0;
                    while (r < runs.Count)
                    {
                        if (!runs[r].Red) { r++; continue; }
                        int first = r, last = r;
                        while (last + 1 < runs.Count && runs[last + 1].Red &&
                               runs[last + 1].Offset == runs[last].Offset + runs[last].Text.Length)
                            last++;
                        aiIndex++;
                        segments.Add((first, last,
                            string.Concat(runs.GetRange(first, last - first + 1).Select(x => x.Text)),
                            "_AI_" + aiIndex));
                        r = last + 1;
                    }

                    // Record suggestions in document order.
                    foreach (var (first, last, text, name) in segments)
                    {
                        suggestions.Add(new AiSuggestion
                        {
                            Id = int.Parse(name.Substring(4)),
                            BookmarkName = name,
                            SuggestionText = text,
                            ParagraphBookmark = paraBookmark,
                            Status = "Pending"
                        });
                    }

                    // Insert bookmarks from the last segment to the first so
                    // earlier insertions cannot shift the child-entity indices
                    // of segments still to be processed. The BookmarkStart must
                    // be inserted before the BookmarkEnd for DocIO to persist
                    // the bookmark on save.
                    for (int s = segments.Count - 1; s >= 0; s--)
                    {
                        var (first, last, _, name) = segments[s];
                        paragraph.ChildEntities.Insert(runs[first].Index, new BookmarkStart(doc, name));
                        // +2: one past the last red run, plus one for the
                        // Start just inserted.
                        paragraph.ChildEntities.Insert(runs[last].Index + 2, new BookmarkEnd(doc, name));
                    }
                }
            }
            return suggestions;
        }

        // ============ Review actions ============

        // Approve: keep the content, remove the red formatting inside the bookmark.
        [HttpPost]
        [EnableCors("AllowAllOrigins")]
        [Route("Approve")]
        public IActionResult Approve([FromBody] ReviewActionParameter param)
        {
            ApplyBookmarkAction(param, range => range.CharacterFormat.TextColor = Color.Black, "Approved");
            return Json(new { result = "approved", bookmark = param.BookmarkName });
        }

        // Reject: remove only the bookmarked AI content.
        [HttpPost]
        [EnableCors("AllowAllOrigins")]
        [Route("Reject")]
        public IActionResult Reject([FromBody] ReviewActionParameter param)
        {
            ApplyBookmarkAction(param, range => range.OwnerParagraph?.ChildEntities.Remove(range), "Rejected");
            return Json(new { result = "rejected", bookmark = param.BookmarkName });
        }

        // Approve All: approve every remaining Pending suggestion.
        [HttpPost]
        [EnableCors("AllowAllOrigins")]
        [Route("ApproveAll")]
        public IActionResult ApproveAll([FromBody] ReviewActionParameter param)
        {
            var suggestions = param.Suggestions ?? LoadSuggestions() ?? new List<AiSuggestion>();
            ApplyBookmarkAction(param, range => range.CharacterFormat.TextColor = Color.Black, null, suggestions);
            foreach (var suggestion in suggestions)
                if (suggestion.Status == "Pending")
                    suggestion.Status = "Approved";
            PersistSuggestions(suggestions);
            return Json(new { result = "approvedAll" });
        }

        // Applies a per-run transform to the content inside each named bookmark,
        // saves the document in place, and updates the suggestion statuses.
        private void ApplyBookmarkAction(ReviewActionParameter param, Action<WTextRange> transform, string newStatus, List<AiSuggestion> suggestions = null)
        {
            var doc = new WDocument(DocumentPath, FormatType.Docx);
            try
            {
                var targets = suggestions != null
                    ? suggestions.Where(s => s.Status == "Pending").Select(s => s.BookmarkName).ToList()
                    : new List<string> { param.BookmarkName };
                foreach (var name in targets)
                {
                    var bookmark = doc.Bookmarks.FindByName(name);
                    if (bookmark == null) continue;
                    var entity = bookmark.BookmarkStart.NextSibling;
                    int guard = 0;
                    while (entity != null && !ReferenceEquals(entity, bookmark.BookmarkEnd) && guard++ < 1000)
                    {
                        if (entity is WTextRange range)
                        {
                            var next = entity.NextSibling;
                            transform(range);
                            entity = ReferenceEquals(next, bookmark.BookmarkEnd) ? null : next;
                        }
                        else
                        {
                            entity = entity.NextSibling;
                        }
                    }
                }
                SaveDocumentInPlace(doc);
            }
            finally
            {
                doc.Close();
            }

            if (newStatus != null)
            {
                var all = suggestions ?? (param.Suggestions ?? LoadSuggestions() ?? new List<AiSuggestion>());
                var target = all.FirstOrDefault(s => s.BookmarkName == param.BookmarkName);
                if (target != null)
                    target.Status = newStatus;
                PersistSuggestions(all);
            }
        }

        // Returns the stored suggestions, or null when the JSON file is missing
        // (distinct from an empty list, which is a deliberate reset).
        private List<AiSuggestion> LoadSuggestions()
        {
            if (System.IO.File.Exists(SuggestionsPath))
            {
                var payload = Newtonsoft.Json.JsonConvert.DeserializeObject<ContentReviewPayload>(System.IO.File.ReadAllText(SuggestionsPath));
                return payload?.Suggestions ?? new List<AiSuggestion>();
            }
            return null;
        }

        private void PersistSuggestions(List<AiSuggestion> suggestions)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new ContentReviewPayload { Suggestions = suggestions });
            System.IO.File.WriteAllText(SuggestionsPath, json);
        }

        // Saves the document back to the same file (caller closes the document).
        private void SaveDocumentInPlace(WDocument doc)
        {
            using (var fs = new FileStream(DocumentPath, FileMode.Create, FileAccess.ReadWrite))
                doc.Save(fs, FormatType.Docx);
        }

        // ============ Save / export ============

        // Save Changes: persist the updated document (from editor SFDT) to the
        // same file and the suggestion statuses. Only the AI_n / Para_n
        // bookmarks added by the review flow are removed; bookmarks that
        // already existed in the document are preserved as-is.
        [HttpPost]
        [EnableCors("AllowAllOrigins")]
        [Route("Save")]
        public IActionResult Save([FromBody] ReviewActionParameter param)
        {
            if (string.IsNullOrEmpty(param.Content))
                return BadRequest();
            var editorDoc = Syncfusion.EJ2.DocumentEditor.WordDocument.Save(param.Content);
            try
            {
                using (var docxStream = new MemoryStream())
                {
                    editorDoc.Save(docxStream, Syncfusion.DocIO.FormatType.Docx);
                    docxStream.Position = 0;
                    var doc = new WDocument(docxStream, FormatType.Docx);
                    try
                    {
                        RemoveReviewBookmarks(doc);
                        SaveDocumentInPlace(doc);
                    }
                    finally
                    {
                        doc.Close();
                    }
                }
            }
            finally
            {
                editorDoc.Close();
            }
            if (param.Suggestions != null)
                PersistSuggestions(param.Suggestions);
            return Json(new { result = "saved" });
        }

        // Export as .docx: return the finalized document. Any suggestion that
        // was not accepted (still Pending) has its AI content removed, and all
        // AI_/Para_ bookmarks added by the review flow are cleared, so the
        // downloaded document is a clean, finalized copy.
        [HttpPost]
        [EnableCors("AllowAllOrigins")]
        [Route("Export")]
        public IActionResult Export([FromBody] ReviewActionParameter param)
        {
            if (string.IsNullOrEmpty(param.Content))
                return BadRequest();
            var suggestions = param.Suggestions ?? LoadSuggestions() ?? new List<AiSuggestion>();

            var editorDoc = Syncfusion.EJ2.DocumentEditor.WordDocument.Save(param.Content);
            try
            {
                using (var docxStream = new MemoryStream())
                {
                    editorDoc.Save(docxStream, Syncfusion.DocIO.FormatType.Docx);
                    docxStream.Position = 0;
                    var doc = new WDocument(docxStream, FormatType.Docx);
                    try
                    {
                        // Finalize: remove pending AI content and review bookmarks.
                        foreach (var suggestion in suggestions)
                        {
                            if (suggestion.Status == "Pending")
                            {
                                RemoveBookmarkedContent(doc, suggestion.BookmarkName);
                                suggestion.Status = "Rejected";
                            }
                        }
                        RemoveReviewBookmarks(doc);

                        var result = new MemoryStream();
                        doc.Save(result, FormatType.Docx);
                        result.Position = 0;
                        PersistSuggestions(suggestions);
                        return new FileStreamResult(result, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
                        {
                            FileDownloadName = string.IsNullOrEmpty(param.FileName) ? "Reviewed.docx" : param.FileName
                        };
                    }
                    finally
                    {
                        doc.Close();
                    }
                }
            }
            finally
            {
                editorDoc.Close();
            }
        }

        // Removes the text content inside the named bookmark (keeps everything else).
        private static void RemoveBookmarkedContent(WDocument doc, string bookmarkName)
        {
            var bookmark = doc.Bookmarks.FindByName(bookmarkName);
            if (bookmark == null) return;
            var toRemove = new List<WTextRange>();
            var entity = bookmark.BookmarkStart.NextSibling;
            int guard = 0;
            while (entity != null && !ReferenceEquals(entity, bookmark.BookmarkEnd) && guard++ < 1000)
            {
                if (entity is WTextRange range)
                    toRemove.Add(range);
                entity = entity.NextSibling;
            }
            foreach (var range in toRemove)
                range.OwnerParagraph?.ChildEntities.Remove(range);
        }

        // Clears every AI_n / Para_n bookmark (start and end entities) from the
        // document body so no review leftovers remain in the saved/exported file.
        private static void RemoveReviewBookmarks(WDocument doc)
        {
            foreach (WSection section in doc.Sections)
            {
                foreach (var paragraph in EnumerateParagraphs(section))
                {
                    var toRemove = new List<Entity>();
                    foreach (var entity in paragraph.ChildEntities)
                    {
                        if (entity is BookmarkStart bs && IsReviewBookmark(bs.Name))
                            toRemove.Add(bs);
                        else if (entity is BookmarkEnd be && IsReviewBookmark(be.Name))
                            toRemove.Add(be);
                    }
                    foreach (var entity in toRemove)
                        paragraph.ChildEntities.Remove(entity);
                }
            }
        }

        private static bool IsReviewBookmark(string name) =>
            name != null && (name.StartsWith("_AI_") || name.StartsWith("_Para_"));

        // Enumerates every paragraph in the section body, including paragraphs
        // inside tables and nested tables (so red AI text in table cells is
        // also considered).
        private static IEnumerable<WParagraph> EnumerateParagraphs(WSection section)
        {
            foreach (var paragraph in EnumerateParagraphs(section.Body.ChildEntities))
                yield return paragraph;
        }

        private static IEnumerable<WParagraph> EnumerateParagraphs(EntityCollection entities)
        {
            foreach (var entity in entities)
            {
                if (entity is WParagraph paragraph)
                    yield return paragraph;
                else if (entity is WTable table)
                    foreach (var nested in EnumerateTableParagraphs(table))
                        yield return nested;
            }
        }

        private static IEnumerable<WParagraph> EnumerateTableParagraphs(WTable table)
        {
            foreach (WTableRow row in table.Rows)
            {
                foreach (WTableCell cell in row.Cells)
                {
                    foreach (var paragraph in EnumerateParagraphs(cell.ChildEntities))
                        yield return paragraph;
                }
            }
        }
    }
}
