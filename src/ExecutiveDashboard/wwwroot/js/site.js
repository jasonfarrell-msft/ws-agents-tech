(() => {
  const immediateForms = document.querySelectorAll("form[data-immediate-loading]");
  if (immediateForms.length === 0) {
    return;
  }

  immediateForms.forEach((form) => {
    if (!(form instanceof HTMLFormElement)) {
      return;
    }

    const loadingIndicator = form.querySelector("[data-loading-indicator]");
    const submitButtons = form.querySelectorAll("button[type='submit'], input[type='submit']");
    let loadingActive = false;

    const setLoadingState = () => {
      if (loadingActive) {
        return;
      }

      loadingActive = true;
      form.classList.add("is-loading");
      form.setAttribute("aria-busy", "true");

      if (loadingIndicator instanceof HTMLElement) {
        loadingIndicator.hidden = false;
      }

      submitButtons.forEach((button) => {
        if (button instanceof HTMLButtonElement || button instanceof HTMLInputElement) {
          button.disabled = true;
        }
      });
    };

    form.addEventListener("submit", () => {
      if (!form.checkValidity()) {
        return;
      }

      setLoadingState();
    });

    form.querySelectorAll("[data-submit-on-change]").forEach((submitOnChangeInput) => {
      submitOnChangeInput.addEventListener("change", () => {
        if (!form.checkValidity()) {
          form.reportValidity();
          return;
        }

        if (typeof form.requestSubmit === "function") {
          form.requestSubmit();
          return;
        }

        setLoadingState();
        form.submit();
      });
    });
  });
})();
