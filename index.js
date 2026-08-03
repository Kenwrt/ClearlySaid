require('dotenv').config();
const express = require('express');
const path = require('path');
const { OpenAI } = require('openai');

const app = express();
const port = process.env.PORT || 3000;

app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));

let openai;
function getOpenAI() {
  if (!openai) {
    openai = new OpenAI({ apiKey: process.env.OPENAI_API_KEY });
  }
  return openai;
}

const CONVERSION_MODES = {
  simplify: 'Rewrite the following text in simpler, clearer language that anyone can understand. Preserve the meaning.',
  formal: 'Rewrite the following text in a formal, professional tone suitable for business communication.',
  casual: 'Rewrite the following text in a casual, friendly, conversational tone.',
  summarize: 'Summarize the following text concisely, capturing only the key points.',
  expand: 'Expand the following text with more detail, examples, and explanation while keeping the original meaning.',
  'fix-grammar': 'Fix any grammar, spelling, and punctuation errors in the following text. Return only the corrected text.',
};

app.post('/api/convert', async (req, res) => {
  const { text, mode } = req.body;

  if (!text || typeof text !== 'string' || text.trim().length === 0) {
    return res.status(400).json({ error: 'Text is required.' });
  }

  if (!mode || !CONVERSION_MODES[mode]) {
    return res.status(400).json({ error: `Invalid mode. Choose one of: ${Object.keys(CONVERSION_MODES).join(', ')}` });
  }

  if (text.length > 5000) {
    return res.status(400).json({ error: 'Text must be 5000 characters or fewer.' });
  }

  try {
    const completion = await getOpenAI().chat.completions.create({
      model: 'gpt-4o-mini',
      messages: [
        { role: 'system', content: CONVERSION_MODES[mode] },
        { role: 'user', content: text.trim() },
      ],
      max_tokens: 1024,
    });

    const result = completion.choices[0]?.message?.content?.trim() ?? '';
    return res.json({ result });
  } catch (err) {
    console.error('OpenAI error:', err.message);
    return res.status(502).json({ error: 'Failed to convert text. Please try again.' });
  }
});

app.get('/api/modes', (req, res) => {
  res.json({ modes: Object.keys(CONVERSION_MODES) });
});

if (require.main === module) {
  app.listen(port, () => {
    console.log(`ClearlySaid running at http://localhost:${port}`);
  });
}

module.exports = app;
