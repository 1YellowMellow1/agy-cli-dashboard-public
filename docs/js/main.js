// AGY CLI Dashboard - Showcase Landing Page JavaScript

document.addEventListener("DOMContentLoaded", () => {
  // 1. Mockup Tab Switcher
  const tabs = document.querySelectorAll(".mockup-tab");
  const contents = document.querySelectorAll(".mockup-content");

  tabs.forEach(tab => {
    tab.addEventListener("click", () => {
      const targetId = tab.getAttribute("data-tab");

      tabs.forEach(t => t.classList.remove("active"));
      contents.forEach(c => c.classList.remove("active"));

      tab.classList.add("active");
      const targetContent = document.getElementById(targetId);
      if (targetContent) {
        targetContent.classList.add("active");
      }
    });
  });

  // 2. Robust Copy Code to Clipboard (with execCommand fallback)
  function copyTextToClipboard(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text).catch(() => fallbackCopy(text));
    }
    return fallbackCopy(text);
  }

  function fallbackCopy(text) {
    return new Promise((resolve, reject) => {
      const textarea = document.createElement("textarea");
      textarea.value = text;
      textarea.style.position = "fixed";
      textarea.style.left = "-9999px";
      textarea.style.top = "0";
      textarea.setAttribute("readonly", "");
      document.body.appendChild(textarea);
      textarea.focus();
      textarea.select();
      try {
        const successful = document.execCommand("copy");
        document.body.removeChild(textarea);
        if (successful) resolve();
        else reject(new Error("execCommand copy failed"));
      } catch (err) {
        document.body.removeChild(textarea);
        reject(err);
      }
    });
  }

  const copyButtons = document.querySelectorAll(".btn-copy");
  copyButtons.forEach(btn => {
    btn.addEventListener("click", () => {
      const textToCopy = btn.getAttribute("data-copy") || "git clone https://github.com/1YellowMellow1/agy-cli-dashboard-public.git";
      copyTextToClipboard(textToCopy).then(() => {
        const originalText = btn.innerHTML;
        const currentLang = (typeof AgyI18n !== 'undefined') ? AgyI18n.getLanguage() : 'en';
        const copiedLabel = currentLang === 'de' ? 'Kopiert' : 'Copied';

        btn.innerHTML = `
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
          <span>${copiedLabel}</span>
        `;
        btn.style.color = "#10b981";
        btn.style.borderColor = "rgba(16, 185, 129, 0.4)";

        setTimeout(() => {
          btn.innerHTML = originalText;
          btn.style.color = "";
          btn.style.borderColor = "";
        }, 2200);
      }).catch(err => {
        console.warn("Clipboard copy could not be completed:", err);
      });
    });
  });

  // 3. FAQ Accordion Toggle
  const faqQuestions = document.querySelectorAll(".faq-question");
  faqQuestions.forEach(btn => {
    btn.addEventListener("click", () => {
      const item = btn.closest(".faq-item");
      const isOpen = item.classList.contains("active");

      // Close all other items
      document.querySelectorAll(".faq-item").forEach(i => i.classList.remove("active"));

      if (!isOpen) {
        item.classList.add("active");
      }
    });
  });

  // 4. Interactive Cockpit Toolbar Guide
  let currentActiveToolbarId = "context";
  const tbButtons = document.querySelectorAll(".tb-btn");
  const tbIcon = document.getElementById("tb-icon");
  const tbName = document.getElementById("tb-name");
  const tbType = document.getElementById("tb-type");
  const tbShortcut = document.getElementById("tb-shortcut");
  const tbDesc = document.getElementById("tb-desc");

  function selectToolbarItem(id) {
    if (typeof AgyI18n === 'undefined') return;
    const item = AgyI18n.getToolbarItem(id);
    if (!item) return;
    currentActiveToolbarId = id;

    tbButtons.forEach(btn => {
      if (btn.getAttribute("data-id") === id) {
        btn.classList.add("active");
      } else {
        btn.classList.remove("active");
      }
    });

    if (tbIcon) tbIcon.innerHTML = item.icon;
    if (tbName) tbName.textContent = item.name;
    if (tbType) tbType.textContent = item.type;
    if (tbShortcut) tbShortcut.textContent = item.shortcut;
    if (tbDesc) tbDesc.textContent = item.desc;
  }

  tbButtons.forEach(btn => {
    btn.addEventListener("click", () => {
      const id = btn.getAttribute("data-id");
      if (id) selectToolbarItem(id);
    });
  });

  // 5. Progressive Disclosure: Feature Matrix Toggle
  const matrixContainer = document.getElementById("feature-matrix");
  const btnToggleMatrix = document.getElementById("btn-toggle-matrix");
  const matrixToggleText = document.getElementById("matrix-toggle-text");
  const matrixToggleCount = document.getElementById("matrix-toggle-count");
  const matrixToggleIcon = document.getElementById("matrix-toggle-icon");

  function updateMatrixButtonLabels() {
    if (!matrixContainer || !btnToggleMatrix) return;
    const isCollapsed = matrixContainer.classList.contains("is-collapsed");
    const lang = (typeof AgyI18n !== 'undefined') ? AgyI18n.getLanguage() : 'en';

    if (isCollapsed) {
      if (matrixToggleText) {
        matrixToggleText.textContent = lang === 'de' 
          ? "Alle 28 Kriterien & Detailvergleiche ausklappen" 
          : "Expand all 28 criteria & detailed comparisons";
      }
      if (matrixToggleCount) {
        matrixToggleCount.textContent = lang === 'de' ? "+21 Kriterien" : "+21 Criteria";
      }
      if (matrixToggleIcon) {
        matrixToggleIcon.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>`;
      }
    } else {
      if (matrixToggleText) {
        matrixToggleText.textContent = lang === 'de' 
          ? "Auf Kern-Highlights minimieren" 
          : "Collapse to core highlights";
      }
      if (matrixToggleCount) {
        matrixToggleCount.textContent = lang === 'de' ? "Kompakt" : "Compact";
      }
      if (matrixToggleIcon) {
        matrixToggleIcon.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="18 15 12 9 6 15"/></svg>`;
      }
    }
  }

  if (matrixContainer && btnToggleMatrix) {
    btnToggleMatrix.addEventListener("click", () => {
      const isCollapsed = matrixContainer.classList.contains("is-collapsed");
      if (isCollapsed) {
        matrixContainer.classList.remove("is-collapsed");
      } else {
        matrixContainer.classList.add("is-collapsed");
        const matrixTop = matrixContainer.getBoundingClientRect().top + window.pageYOffset - 90;
        window.scrollTo({ top: matrixTop, behavior: "smooth" });
      }
      updateMatrixButtonLabels();
    });
  }

  // 6. Connect with i18n changes
  if (typeof AgyI18n !== 'undefined') {
    AgyI18n.onLanguageChange(() => {
      selectToolbarItem(currentActiveToolbarId);
      updateMatrixButtonLabels();
    });
    // Initial sync
    selectToolbarItem(currentActiveToolbarId);
    updateMatrixButtonLabels();
  }
});
