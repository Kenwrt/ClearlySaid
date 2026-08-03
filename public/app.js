(function () {
  const convertBtn = document.getElementById('convertBtn');
  const inputText = document.getElementById('inputText');
  const outputText = document.getElementById('outputText');
  const modeSelect = document.getElementById('mode');
  const errorDiv = document.getElementById('error');
  const loadingDiv = document.getElementById('loading');
  const charCount = document.getElementById('charCount');
  const copyBtn = document.getElementById('copyBtn');

  inputText.addEventListener('input', function () {
    charCount.textContent = this.value.length;
  });

  copyBtn.addEventListener('click', function () {
    const text = outputText.value;
    if (!text) return;
    navigator.clipboard.writeText(text).then(function () {
      copyBtn.textContent = 'Copied!';
      setTimeout(function () { copyBtn.textContent = 'Copy'; }, 2000);
    });
  });

  convertBtn.addEventListener('click', async function () {
    const text = inputText.value.trim();
    const mode = modeSelect.value;

    errorDiv.classList.add('hidden');
    errorDiv.textContent = '';
    outputText.value = '';

    if (!text) {
      showError('Please enter some text to convert.');
      return;
    }

    setLoading(true);

    try {
      const response = await fetch('/api/convert', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text, mode }),
      });

      const data = await response.json();

      if (!response.ok) {
        showError(data.error || 'An unexpected error occurred.');
        return;
      }

      outputText.value = data.result;
    } catch (err) {
      showError('Network error. Please check your connection and try again.');
    } finally {
      setLoading(false);
    }
  });

  function showError(msg) {
    errorDiv.textContent = msg;
    errorDiv.classList.remove('hidden');
  }

  function setLoading(isLoading) {
    convertBtn.disabled = isLoading;
    loadingDiv.classList.toggle('hidden', !isLoading);
  }
})();
