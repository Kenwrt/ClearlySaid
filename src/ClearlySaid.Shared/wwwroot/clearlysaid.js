window.clearlySaid = {
    copyText: async function (text) {
        await navigator.clipboard.writeText(text);
    }
};

document.addEventListener("copy", function (event) {
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed || !event.clipboardData) {
        return;
    }

    const anchor = selection.anchorNode instanceof Element
        ? selection.anchorNode
        : selection.anchorNode?.parentElement;
    const focus = selection.focusNode instanceof Element
        ? selection.focusNode
        : selection.focusNode?.parentElement;

    if (!anchor?.closest(".cs-result p") || !focus?.closest(".cs-result p")) {
        return;
    }

    event.preventDefault();
    event.clipboardData.setData("text/plain", selection.toString());
});
