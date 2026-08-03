window.clearlySaid = {
    copyText: async function (text) {
        await navigator.clipboard.writeText(text);
    }
};
