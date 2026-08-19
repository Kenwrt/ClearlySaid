(function () {
    let completeRegistrationTracked = false;

    window.clearlySaidMeta = {
        trackCompleteRegistration: function () {
            if (completeRegistrationTracked || typeof window.fbq !== "function") {
                return;
            }

            completeRegistrationTracked = true;
            window.fbq("track", "CompleteRegistration");
        }
    };
})();
