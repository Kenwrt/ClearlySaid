window.clearlySaid = {
    copyText: async function (text) {
        await navigator.clipboard.writeText(text);
    },
    downloadAuthorized: async function (url, token, fileName) {
        const response = await fetch(url, {
            headers: { Authorization: `Bearer ${token}` }
        });
        if (!response.ok) {
            throw new Error("The Android test app could not be downloaded.");
        }

        const objectUrl = URL.createObjectURL(await response.blob());
        const link = document.createElement("a");
        link.href = objectUrl;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(objectUrl);
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
