document.addEventListener("DOMContentLoaded", () => {
  const liveStatus = document.getElementById("liveStatus");
  let liveStatusTimer = null;

  const showLiveStatus = (message, variant = "success") => {
    if (!liveStatus) {
      return;
    }

    liveStatus.className = `live-status live-status--${variant}`;
    liveStatus.textContent = message;
    liveStatus.classList.remove("d-none");

    if (liveStatusTimer) {
      window.clearTimeout(liveStatusTimer);
    }

    liveStatusTimer = window.setTimeout(() => {
      liveStatus.classList.add("d-none");
    }, 1800);
  };

  const searchToggle = document.querySelector("[data-search-toggle]");
  const searchPanel = document.getElementById("searchPanel");
  const tableCard = document.querySelector(".table-card");
  const editableCellSelector = ".editable-cell[data-inline-field]";

  const getAntiForgeryToken = () => {
    const tokenField = document.querySelector('input[name="__RequestVerificationToken"]');
    return tokenField instanceof HTMLInputElement ? tokenField.value : "";
  };

  const getTableMeta = () => ({
    search: tableCard?.dataset.search ?? "",
    pageNumber: tableCard?.dataset.pageNumber ?? "1",
    pageSize: tableCard?.dataset.pageSize ?? "25",
  });

  const setSearchPanelOpen = (open) => {
    if (!searchToggle || !searchPanel) {
      return;
    }

    searchPanel.hidden = !open;
    searchToggle.classList.toggle("topbar-search-toggle--active", open);
    searchToggle.setAttribute("aria-expanded", String(open));
    searchToggle.textContent = open ? "Đóng tìm kiếm" : "Tìm kiếm";

    if (open) {
      const searchInput = searchPanel.querySelector("#search");
      window.setTimeout(() => searchInput?.focus(), 50);
    }
  };

  if (searchToggle && searchPanel) {
    setSearchPanelOpen(false);

    searchToggle.addEventListener("click", () => {
      setSearchPanelOpen(searchPanel.hidden);
    });

    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && !searchPanel.hidden) {
        setSearchPanelOpen(false);
        searchToggle.focus();
      }
    });
  }

  const renderSuggestedTranslationCell = (cell, value) => {
    cell.innerHTML = "";

    const normalizedValue = value ?? "";
    if (normalizedValue.trim().length === 0) {
      const placeholder = document.createElement("span");
      placeholder.className = "text-muted editable-placeholder";
      placeholder.textContent = "Nhấp đúp để nhập";
      cell.appendChild(placeholder);
      return;
    }

    const display = document.createElement("div");
    display.className = "cell-snippet cell-snippet--full editable-display";
    display.title = normalizedValue;
    display.textContent = normalizedValue;
    cell.appendChild(display);
  };

  const renderRatingCell = (cell, value) => {
    cell.innerHTML = "";

    const normalizedValue = value ?? "";
    if (normalizedValue.trim().length === 0) {
      const placeholder = document.createElement("span");
      placeholder.className = "text-muted editable-placeholder";
      placeholder.textContent = "Nhấp đúp để chấm";
      cell.appendChild(placeholder);
      return;
    }

    const display = document.createElement("span");
    display.className = "rating-pill editable-display";
    display.textContent = normalizedValue;
    cell.appendChild(display);
  };

  const renderCellValue = (cell, field, value) => {
    cell.dataset.originalValue = value ?? "";

    if (field === "rating") {
      renderRatingCell(cell, value);
      return;
    }

    renderSuggestedTranslationCell(cell, value);
  };

  const getRowValue = (row, field) => {
    if (field === "rating") {
      return row.dataset.rating ?? "";
    }

    return row.dataset.suggestedTranslation ?? "";
  };

  const setRowValue = (row, field, value) => {
    if (field === "rating") {
      row.dataset.rating = value ?? "";
      return;
    }

    row.dataset.suggestedTranslation = value ?? "";
  };

  const updateEditableCell = (row, field, value) => {
    const cell = row.querySelector(`${editableCellSelector}[data-inline-field="${field}"]`);
    if (!cell) {
      return;
    }

    renderCellValue(cell, field, value);
  };

  let activeEditor = null;

  const finishEditor = (state) => {
    if (state.finished) {
      return;
    }

    state.finished = true;

    if (activeEditor === state) {
      activeEditor = null;
    }

    state.cell.classList.remove("cell-editing", "cell-editing--saving");
    state.cell.removeAttribute("data-editing");
  };

  const cancelEditor = (state) => {
    if (state.finished) {
      return;
    }

    state.cancelled = true;
    finishEditor(state);
    renderCellValue(state.cell, state.field, getRowValue(state.row, state.field));
  };

  const commitEditor = async (state) => {
    if (!state || state.finished || state.saving || state.cancelled) {
      return;
    }

    const editor = state.editor;
    const rawValue = editor?.value ?? "";
    const trimmedValue = rawValue.trim();
    const currentSuggested = getRowValue(state.row, "suggestedTranslation");
    const currentRating = getRowValue(state.row, "rating");

    if (state.field === "rating" && trimmedValue.length > 0 && Number.isNaN(Number(trimmedValue))) {
      showLiveStatus("Rating phải là một số hợp lệ.", "warning");
      editor?.focus();
      return;
    }

    if (state.field === "suggestedTranslation" && trimmedValue === currentSuggested) {
      cancelEditor(state);
      return;
    }

    if (state.field === "rating" && trimmedValue === currentRating) {
      cancelEditor(state);
      return;
    }

    state.saving = true;
    state.cell.classList.add("cell-editing--saving");
    if (editor) {
      editor.disabled = true;
    }

    const payload = new FormData();
    payload.append("id", state.row.dataset.rowId ?? "");
    payload.append(
      "suggestedTranslation",
      state.field === "suggestedTranslation" ? trimmedValue : currentSuggested,
    );
    payload.append(
      "rating",
      state.field === "rating" ? trimmedValue : currentRating,
    );

    const meta = getTableMeta();
    payload.append("search", meta.search);
    payload.append("pageNumber", meta.pageNumber);
    payload.append("pageSize", meta.pageSize);

    const token = getAntiForgeryToken();
    if (token) {
      payload.append("__RequestVerificationToken", token);
    }

    try {
      const response = await fetch(`${window.location.pathname}?handler=Update`, {
        method: "POST",
        body: payload,
        headers: {
          "X-Requested-With": "XMLHttpRequest",
          Accept: "application/json",
        },
        credentials: "same-origin",
      });

      let result = null;
      try {
        result = await response.json();
      } catch {
        result = null;
      }

      if (!response.ok || !result || result.success !== true) {
        throw new Error(result?.message || "Không thể lưu thay đổi.");
      }

      const normalizedSuggested = result.suggestedTranslation ?? (state.field === "suggestedTranslation" ? trimmedValue : currentSuggested);
      const normalizedRating = result.ratingDisplay ?? (state.field === "rating" ? trimmedValue : currentRating);

      setRowValue(state.row, "suggestedTranslation", normalizedSuggested);
      setRowValue(state.row, "rating", normalizedRating);

      finishEditor(state);
      updateEditableCell(state.row, state.field, state.field === "suggestedTranslation" ? normalizedSuggested : normalizedRating);
      showLiveStatus(result.message || "Đã cập nhật dữ liệu.", "success");
    } catch (error) {
      console.error(error);
      state.saving = false;
      state.cell.classList.remove("cell-editing--saving");
      if (editor) {
        editor.disabled = false;
        editor.focus();
      }
      showLiveStatus(error?.message || "Không thể lưu thay đổi. Hãy thử lại.", "danger");
      return;
    }
  };

  const startInlineEdit = async (cell) => {
    if (!cell || cell.classList.contains("cell-editing")) {
      return;
    }

    if (activeEditor && activeEditor.cell !== cell) {
      await commitEditor(activeEditor);
      if (activeEditor) {
        return;
      }
    }

    const row = cell.closest("tr");
    if (!row) {
      return;
    }

    const field = cell.dataset.inlineField;
    if (!field) {
      return;
    }

    const originalValue = getRowValue(row, field);
    const state = {
      cell,
      row,
      field,
      editor: null,
      saving: false,
      cancelled: false,
      finished: false,
    };

    activeEditor = state;
    cell.classList.add("cell-editing");
    cell.dataset.editing = "true";
    cell.innerHTML = "";

    if (field === "rating") {
      const input = document.createElement("input");
      input.type = "number";
      input.step = "0.1";
      input.inputMode = "decimal";
      input.className = "inline-editor-control inline-editor-control--number form-control";
      input.value = originalValue;

      input.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
          event.preventDefault();
          event.stopPropagation();
          cancelEditor(state);
          return;
        }

        if (event.key === "Enter") {
          event.preventDefault();
          event.stopPropagation();
          void commitEditor(state);
        }
      });

      input.addEventListener("blur", () => {
        void commitEditor(state);
      });

      cell.appendChild(input);
      state.editor = input;

      window.requestAnimationFrame(() => {
        input.focus();
        input.select();
      });
      return;
    }

    const textarea = document.createElement("textarea");
    textarea.className = "inline-editor-control inline-editor-control--textarea form-control";
    textarea.rows = Math.max(4, Math.min(12, originalValue.split(/\r?\n/).length + 1));
    textarea.spellcheck = true;
    textarea.value = originalValue;

    const resizeTextarea = () => {
      textarea.style.height = "auto";
      textarea.style.height = `${Math.min(textarea.scrollHeight, 420)}px`;
    };

    textarea.addEventListener("input", resizeTextarea);
    textarea.addEventListener("keydown", (event) => {
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        cancelEditor(state);
        return;
      }

      if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
        event.preventDefault();
        event.stopPropagation();
        void commitEditor(state);
      }
    });

    textarea.addEventListener("blur", () => {
      void commitEditor(state);
    });

    cell.appendChild(textarea);
    state.editor = textarea;

    window.requestAnimationFrame(() => {
      resizeTextarea();
      textarea.focus();
      textarea.setSelectionRange(0, textarea.value.length);
    });
  };

  document.addEventListener("dblclick", (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
      return;
    }

    const cell = target.closest(editableCellSelector);
    if (!cell) {
      return;
    }

    event.preventDefault();
    void startInlineEdit(cell);
  });

  document.addEventListener("submit", async (event) => {
    const form = event.target;

    if (!(form instanceof HTMLFormElement) || !form.classList.contains("lock-toggle-form")) {
      return;
    }

    event.preventDefault();

    const button = form.querySelector(".lock-toggle-button");
    if (!(button instanceof HTMLButtonElement)) {
      return;
    }

    const originalLabel = button.textContent?.trim() || "Mở";
    button.disabled = true;
    button.setAttribute("aria-busy", "true");
    button.classList.add("state-pill--loading");
    button.textContent = "Đang lưu...";

    try {
      const response = await fetch(form.action, {
        method: "POST",
        body: new FormData(form),
        headers: {
          "X-Requested-With": "XMLHttpRequest",
          Accept: "application/json",
        },
        credentials: "same-origin",
      });

      let payload = null;
      try {
        payload = await response.json();
      } catch {
        payload = null;
      }

      if (!response.ok || !payload || payload.success !== true) {
        button.textContent = originalLabel;
        showLiveStatus(payload?.message || "Không thể đổi trạng thái khóa.", "danger");
        return;
      }

      const isLocked = Boolean(payload.locked);
      button.textContent = payload.label || (isLocked ? "Khóa" : "Mở");
      button.classList.toggle("state-pill--locked", isLocked);
      button.dataset.locked = String(isLocked);
      showLiveStatus(payload.message || "Đã cập nhật trạng thái.", "success");
    } catch (error) {
      console.error(error);
      button.textContent = originalLabel;
      showLiveStatus("Không thể đổi trạng thái khóa. Hãy thử lại.", "danger");
    } finally {
      button.disabled = false;
      button.removeAttribute("aria-busy");
      button.classList.remove("state-pill--loading");
    }
  });
});
