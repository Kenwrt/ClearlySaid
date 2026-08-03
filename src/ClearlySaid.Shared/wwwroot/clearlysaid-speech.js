let recognition;
let dotnetReference;

export function isSupported() {
    return Boolean(window.SpeechRecognition || window.webkitSpeechRecognition);
}

export function start(reference) {
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
        throw new Error("Speech recognition isn't supported by this browser.");
    }

    dotnetReference = reference;
    recognition = new Recognition();
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = navigator.language || "en-US";

    recognition.onresult = event => {
        let transcript = "";
        for (let index = 0; index < event.results.length; index++) {
            transcript += event.results[index][0].transcript;
        }
        dotnetReference.invokeMethodAsync("ReceiveTranscript", transcript.trim());
    };

    recognition.onerror = event => {
        dotnetReference.invokeMethodAsync("ReceiveError", event.error || "Speech recognition failed.");
    };

    recognition.onend = () => {
        dotnetReference.invokeMethodAsync("ReceiveStopped");
    };

    recognition.start();
}

export function stop() {
    recognition?.stop();
}
