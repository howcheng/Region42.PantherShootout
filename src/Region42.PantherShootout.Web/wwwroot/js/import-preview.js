// Schedule Preview & Override interactions
(function () {
	"use strict";

	let overridesDirty = false;

	function initialize() {
		const overrideSelects = document.querySelectorAll(".bracket-override-select");
		overrideSelects.forEach(function (select) {
			select.addEventListener("change", handleOverrideChanged);
		});

		const confirmForm = document.getElementById("confirmImportForm");
		if (confirmForm) {
			confirmForm.addEventListener("submit", handleConfirmSubmit);
		}
	}

	function handleOverrideChanged() {
		overridesDirty = true;
		const dirtyNotice = document.getElementById("overrideDirtyNotice");
		if (dirtyNotice) {
			dirtyNotice.classList.remove("d-none");
		}
	}

	function handleConfirmSubmit(event) {
		const overwriteCheckbox = document.getElementById("allowOverwriteScores");
		if (overwriteCheckbox && overwriteCheckbox.dataset.required === "true" && !overwriteCheckbox.checked) {
			event.preventDefault();
			globalThis.alert("Recorded scores exist in this tournament year. Check the confirmation box to overwrite existing scores.");
			overwriteCheckbox.focus();
			return;
		}

		if (overridesDirty) {
			const proceed = globalThis.confirm(
				"You changed bracket cell overrides without clicking 'Re-validate Overrides'. Confirming now will validate and apply your current dropdown selections. Continue?"
			);
			if (!proceed) {
				event.preventDefault();
				return;
			}
		}

		copyOverridesIntoConfirmForm();

		const confirmBtn = document.getElementById("confirmImportBtn");
		const spinner = document.getElementById("confirmBtnSpinner");
		const icon = document.getElementById("confirmBtnIcon");
		if (confirmBtn) {
			confirmBtn.disabled = true;
		}
		if (spinner) {
			spinner.classList.remove("d-none");
		}
		if (icon) {
			icon.classList.add("d-none");
		}
	}

	function copyOverridesIntoConfirmForm() {
		const container = document.getElementById("confirmHiddenOverrides");
		if (!container) {
			return;
		}

		container.innerHTML = "";
		const rows = document.querySelectorAll("[data-override-index]");
		rows.forEach(function (row, idx) {
			const cellRef = row.getAttribute("data-cell-reference") || "";
			const select = row.querySelector(".bracket-override-select");
			const selection = select ? select.value : "auto";

			const cellInput = document.createElement("input");
			cellInput.type = "hidden";
			cellInput.name = `overrides[${idx}].CellReference`;
			cellInput.value = cellRef;

			const selInput = document.createElement("input");
			selInput.type = "hidden";
			selInput.name = `overrides[${idx}].Selection`;
			selInput.value = selection;

			container.appendChild(cellInput);
			container.appendChild(selInput);
		});
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
