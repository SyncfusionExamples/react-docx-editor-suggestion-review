# Syncfusion® React DOCX Editor – AI Content Review Workflow

## Introduction

This sample demonstrates an AI-assisted content review workflow using Syncfusion® React DOCX Editor. AI-generated content is identified by red text within a DOCX document and presented as reviewable suggestions. Users can navigate through suggestions, approve or reject individual suggestions, approve all remaining suggestions, save review progress, and export the finalized document.

## Project Features

- Review AI-generated content identified by red-colored text.
- Navigate through suggestions using Previous and Next actions.
- Approve suggestions and retain content in the document.
- Reject suggestions and remove only the AI-generated content.
- Approve all pending suggestions in a single action.
- Save review decisions and export the finalized DOCX document.

## Demo

<!-- TODO: Add workflow screenshot or animated GIF demonstrating the AI content review process. -->

## How to Run the Sample

This sample contains a React client application and an ASP.NET Core backend service. Both applications must be running for the complete workflow.

### 1. Start the ASP.NET Core Backend

Navigate to the server project directory:

```bash
cd Server-Side/src
```

Restore dependencies and run the application:

```bash
dotnet restore
dotnet run
```

### 2. Start the React Application

Navigate to the client project directory:

```bash
cd Client-Side
```

Install dependencies:

```bash
npm install
```

Start the development server:

```bash
npm run dev
```

Open the URL displayed in the terminal.

## APIs Used

| API / Endpoint | HTTP Method | Purpose |
| --- | --- | --- |
| `/api/ContentReview/LoadDocument` | POST | Loads the review document and generates AI suggestions from red text. |
| `/api/ContentReview/GetSuggestions` | GET | Retrieves the generated AI suggestions for the review panel. |
| `/api/ContentReview/ResetSuggestions` | POST | Clears all stored suggestions when a new document is created. |
| `/api/ContentReview/Save` | POST | Saves the reviewed document and suggestion statuses. |
| `/api/ContentReview/Export` | POST | Exports the finalized DOCX document. |

## Resources

- **Product page:** [Syncfusion React DOCX Editor](https://www.syncfusion.com/docx-editor-sdk/react-docx-editor?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples)
- **Documentation:** [Syncfusion React DOCX Editor - Documentation](https://help.syncfusion.com/document-processing/word/word-processor/react/overview?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples)
- **Online demo:** [Syncfusion React DOCX Editor - Online demo](https://document.syncfusion.com/demos/docx-editor/react/#/tailwind3/document-editor/default?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples)

## Support and feedback

For any other queries, reach our [Syncfusion support team](https://support.syncfusion.com/?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples) or post the queries through the [community forums](https://www.syncfusion.com/forums?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples). 

Request new feature through [Syncfusion feedback portal](https://www.syncfusion.com/feedback?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples). 

## License

This is a commercial product and requires a paid license for possession or use. Syncfusion's licensed software, including this component, is subject to the terms and conditions of [Syncfusion's EULA](https://www.syncfusion.com/license/studio/35.1.37/syncfusion_essential_studio_eula.pdf?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples). You can purchase a license [here](https://www.syncfusion.com/sales/products?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples) or start a free 30-day trial [here](https://www.syncfusion.com/account/manage-trials/start-trials?utm_source=github&utm_medium=listing&utm_campaign=github-github-documenteditor-examples).