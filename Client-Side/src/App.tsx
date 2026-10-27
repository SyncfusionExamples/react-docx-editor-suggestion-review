import * as React from 'react';
import {
  DocumentEditorContainerComponent,
  Toolbar, Ribbon
} from '@syncfusion/ej2-react-documenteditor';
import './App.css';

DocumentEditorContainerComponent.Inject(Toolbar, Ribbon);

const SERVICE_URL = import.meta.env.VITE_SERVICE_URL ?? 'http://localhost:62870/api/documenteditor/';
const REVIEW_URL = import.meta.env.VITE_REVIEW_URL ?? 'http://localhost:62870/api/ContentReview/';

interface AiSuggestion {
  Id: number;
  BookmarkName: string;
  SuggestionText: string;
  ParagraphBookmark: string;
  Status: string; // Pending | Approved | Rejected
}

function App() {
  const containerRef = React.useRef<DocumentEditorContainerComponent | null>(null);
  // Only the suggestions still awaiting a decision are shown in the panel;
  // resolved ones are removed from it.
  const [suggestions, setSuggestions] = React.useState<AiSuggestion[]>([]);
  // Full suggestion list as last fetched from the server (superset used to
  // build Save/Export payloads with the decisions taken this session).
  const allSuggestions = React.useRef<AiSuggestion[]>([]);
  // Decisions taken this session (bookmark -> Approved/Rejected), merged into
  // the Save/Export payloads so statuses persist without cluttering the panel.
  const resolved = React.useRef<{ [bookmark: string]: string }>({});
  const [current, setCurrent] = React.useState(0);
  const [loading, setLoading] = React.useState(true);
  // Guards documentChange while we open a scanned document ourselves.
  const applyingScan = React.useRef(false);
  const hasSuggestions = suggestions.length > 0;

  // Load document first, then request the suggestions JSON.
  React.useEffect(() => {
    const load = async () => {
      try {
        // 1. Load the bookmarked document as SFDT and open it in the editor.
        //    (When opened via the toolbar Open button, the Import API already
        //    scanned it for red AI text and prepared the suggestions JSON.)
        const docResponse = await fetch(REVIEW_URL + 'LoadDocument', { method: 'POST' });
        const sfdt = await docResponse.json();
        const editor = containerRef.current?.documentEditor;
        if (editor) {
          applyingScan.current = true;
          editor.open(sfdt);
          applyingScan.current = false;
        }

        // 2. Fetch the suggestions JSON and build the review panel.
        const suggResponse = await fetch(REVIEW_URL + 'GetSuggestions');
        const payload = await suggResponse.json();
        const fetched = (payload.Suggestions ?? []) as AiSuggestion[];
        allSuggestions.current = fetched;
        setSuggestions(fetched);
      } finally {
        setLoading(false);
      }
    };
    load();
  }, []);

  // Refresh the panel whenever the document changes in the editor:
  // - Open button: Import already scanned the document server-side, so just
  //   fetch the prepared suggestions JSON.
  // - New document: no document loaded, reset the panel to 0 suggestions.
  const onDocumentChange = async () => {
    if (applyingScan.current || loading) return;
    const editor = containerRef.current?.documentEditor;
    if (!editor) return;
    try {
      if (editor.documentName) {
        // A document was opened (via Import) or re-loaded: pull the
        // suggestions prepared by the server during import.
        const suggResponse = await fetch(REVIEW_URL + 'GetSuggestions');
        const payload = await suggResponse.json();
        const fetched = (payload.Suggestions ?? []) as AiSuggestion[];
        allSuggestions.current = fetched;
        resolved.current = {};
        setSuggestions(fetched);
      } else {
        // New/empty document: no AI content, clear the panel.
        await fetch(REVIEW_URL + 'ResetSuggestions', { method: 'POST' });
        allSuggestions.current = [];
        resolved.current = {};
        setSuggestions([]);
      }
      setCurrent(0);
    } catch {
      // refresh is best-effort in this POC
    }
  };

  const pending = suggestions.filter(s => s.Status === 'Pending');
  const currentSuggestion =
    suggestions.length > 0
      ? suggestions[Math.min(current, suggestions.length - 1)]
      : null;

  // Navigate the editor to the bookmark of the given suggestion.
  const navigateTo = (suggestion: AiSuggestion) => {
    const editor = containerRef.current?.documentEditor;
    if (!editor) return;
    try {
      editor.selection.selectBookmark(suggestion.BookmarkName, true);
    } catch {
      // navigation is best-effort in this POC
    }
  };

  const selectSuggestion = (index: number) => {
    setCurrent(index);
    if (suggestions[index]) navigateTo(suggestions[index]);
  };

  const move = (delta: number) => {
    if (!suggestions.length) return;
    let next = (current + delta) % suggestions.length;
    if (next < 0) next = suggestions.length - 1;
    selectSuggestion(next);
  };

  const post = (action: string, body: Record<string, unknown>) =>
    fetch(REVIEW_URL + action, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });

  // Apply the action to the editor's live document too, so the view and the
  // SFDT sent on save/export both reflect the decision.
  const applyInEditor = (suggestion: AiSuggestion, action: 'Approve' | 'Reject') => {
    const editor = containerRef.current?.documentEditor;
    if (!editor) return;
    editor.selection.selectBookmark(suggestion.BookmarkName, false);
    if (action === 'Approve') {
      if (editor.selection.contextType === "TableText") {
        editor.selection.characterFormat.fontColor = '#000000';
      } else {
        editor.selection.characterFormat.fontColor = 'default';
      }
    } else {
      // remove only the AI content
      editor.editor.delete();
    }
    editor.editor.deleteBookmark(suggestion.BookmarkName);
  };

  const act = (action: 'Approve' | 'Reject', suggestion: AiSuggestion) => {
    // Client-side only: the change is applied to the editor; the suggestion
    // is removed from the panel. Nothing is persisted to the server document
    // until Save Changes / Export sends the full SFDT + suggestion statuses.
    applyInEditor(suggestion, action);
    resolved.current[suggestion.BookmarkName] = action === 'Approve' ? 'Approved' : 'Rejected';
    const next = suggestions.filter(s => s.BookmarkName !== suggestion.BookmarkName);
    setSuggestions(next);
    setCurrent(idx => Math.min(idx, Math.max(next.length - 1, 0)));
  };

  const approveAll = () => {
    pending.forEach(suggestion => {
      applyInEditor(suggestion, 'Approve');
      resolved.current[suggestion.BookmarkName] = 'Approved';
    });
    setSuggestions([]);
    setCurrent(0);
  };

  const getSfdt = () => containerRef.current?.documentEditor?.serialize();

  // Full suggestion list with this session's decisions applied, for payloads.
  const suggestionsForServer = () =>
    allSuggestions.current.map(s => ({
      ...s,
      Status: resolved.current[s.BookmarkName] ?? s.Status
    }));

  const saveChanges = async () => {
    await post('Save', { Content: getSfdt(), Suggestions: suggestionsForServer(), FileName: 'review.docx' });
  };

  const exportDocx = async () => {
    const response = await fetch(REVIEW_URL + 'Export', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ Content: getSfdt(), Suggestions: suggestionsForServer(), FileName: 'Reviewed.docx' })
    });
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'Reviewed.docx';
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="app-layout">
      <div className="top-bar">
        <button className="export-btn" onClick={exportDocx}>Export as .docx</button>
      </div>

      <div className="suggestion-panel">
        <div className="suggestion-header">
          <div className="left-section">
            <div className="suggestion-title">SUGGESTIONS</div>
            <div className="remaining">{pending.length} remaining</div>
            <div className={'arrow' + (suggestions.length < 2 ? ' disabled' : '')}
              onClick={() => suggestions.length > 1 && move(-1)}>&#10094;</div>
            <div className={'arrow' + (suggestions.length < 2 ? ' disabled' : '')}
              onClick={() => suggestions.length > 1 && move(1)}>&#10095;</div>
          </div>
          <div className="right-actions">
            <button
              className="secondary-btn"
              disabled={!hasSuggestions}
              onClick={approveAll}
            >
              Approve All Suggestions
            </button>
            <div className="link-action" onClick={saveChanges}>Save Changes</div>
          </div>
        </div>

        <div
          className={'ai-content' + (currentSuggestion ? '' : ' empty')}
          onClick={() => currentSuggestion && navigateTo(currentSuggestion)}
          title={currentSuggestion ? 'Click to navigate to this suggestion in the document' : undefined}>
          {currentSuggestion
            ? currentSuggestion.SuggestionText
            : 'No suggestions - review complete.'}
        </div>
        <div className="action-row">
          <button
            className="approve-btn"
            disabled={!hasSuggestions}
            onClick={() => currentSuggestion && act('Approve', currentSuggestion)}
          >
            Approve ✓
          </button>
          <button
            className="reject-btn"
            disabled={!hasSuggestions}
            onClick={() => currentSuggestion && act('Reject', currentSuggestion)}
          >
            Reject ✕
          </button>
        </div>
      </div>

      {loading ? (
        <div className="editor-loading">Loading document...</div>
      ) : null}
      <div className="editor-host">
        <DocumentEditorContainerComponent
          id="container"
          ref={containerRef}
          serviceUrl={SERVICE_URL}
          height='100%'
          enableToolbar={true}
          toolbarMode="Ribbon"
          documentChange={onDocumentChange}
        />
      </div>
    </div>
  );
}

export default App;
