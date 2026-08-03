# ClearlySaid

A simple AI-powered web application for converting and transforming text using the OpenAI API.

## Features

- **Simplify** – rewrite text in plain, easy-to-understand language
- **Make Formal** – convert casual writing to a professional tone
- **Make Casual** – turn stiff prose into friendly conversation
- **Summarize** – distil long passages into key points
- **Expand** – add detail and examples to brief text
- **Fix Grammar** – correct spelling, grammar, and punctuation

## Getting Started

### Prerequisites

- Node.js ≥ 18
- An [OpenAI API key](https://platform.openai.com/account/api-keys)

### Installation

```bash
npm install
```

### Configuration

Copy the example environment file and add your key:

```bash
cp .env.example .env
# edit .env and set OPENAI_API_KEY=sk-...
```

### Running

```bash
npm start
# Open http://localhost:3000 in your browser
```

### Testing

```bash
npm test
```

## Project Structure

```
index.js          – Express server & API routes
public/
  index.html      – Main UI
  style.css       – Stylesheet
  app.js          – Frontend JavaScript
tests/
  api.test.js     – Unit tests (Node built-in test runner)
.env.example      – Environment variable template
```
