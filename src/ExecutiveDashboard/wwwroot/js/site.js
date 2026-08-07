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

(() => {
  const region = document.querySelector("[data-dashboard-region]");
  if (!(region instanceof HTMLElement)) {
    return;
  }

  const dashboardUrl = region.dataset.dashboardUrl;
  if (!dashboardUrl) {
    return;
  }

  const loadingMarkup = region.innerHTML;

  const loadDashboard = async (isRetry = false) => {
    region.innerHTML = loadingMarkup;
    region.setAttribute("aria-busy", "true");
    const loadingStatus = region.querySelector(".dashboard-loading-status");
    if (isRetry && loadingStatus instanceof HTMLElement) {
      loadingStatus.textContent = "Retrying dashboard metrics…";
      loadingStatus.tabIndex = -1;
      loadingStatus.focus();
    }

    try {
      const response = await fetch(dashboardUrl, {
        headers: { "X-Requested-With": "XMLHttpRequest" },
        cache: "no-store",
      });
      if (!response.ok) {
        throw new Error(`Dashboard request failed with status ${response.status}.`);
      }

      region.innerHTML = await response.text();
      region.setAttribute("aria-busy", "false");
    } catch {
      region.innerHTML = `
        <aside class="dashboard-alert" role="alert">
          <strong>Dashboard metrics could not be loaded.</strong>
          <span>Work IQ did not complete the request. Retry when the connection is available.</span>
          <button type="button" class="btn btn-outline-primary btn-sm" data-dashboard-retry>Retry</button>
        </aside>`;
      region.setAttribute("aria-busy", "false");
    }
  };

  region.addEventListener("click", (event) => {
    if (!(event.target instanceof Element) || !event.target.closest("[data-dashboard-retry]")) {
      return;
    }

    loadDashboard(true);
  });

  loadDashboard();
})();
