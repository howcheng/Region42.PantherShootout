// Schedule Upload page interactions
(function () {
	"use strict";

	const CREATE_NEW_YEAR_VALUE = "0";

	function initialize() {
		const yearSelect = document.getElementById("selectedTournamentYearId");
		const newYearInput = document.getElementById("newYear");
		const uploadForm = document.getElementById("scheduleUploadForm");

		if (yearSelect) {
			toggleNewYearFields();
			yearSelect.addEventListener("change", toggleNewYearFields);
		}

		if (newYearInput) {
			newYearInput.addEventListener("input", updateDefaultNamePlaceholder);
		}

		if (uploadForm) {
			uploadForm.addEventListener("submit", handleUploadSubmit);
		}
	}

	function toggleNewYearFields() {
		const yearSelect = document.getElementById("selectedTournamentYearId");
		const newYearContainer = document.getElementById("newYearContainer");
		if (!yearSelect || !newYearContainer) {
			return;
		}

		const isCreateNew = yearSelect.value === CREATE_NEW_YEAR_VALUE;
		newYearContainer.classList.toggle("d-none", !isCreateNew);
	}

	function updateDefaultNamePlaceholder() {
		const newYearInput = document.getElementById("newYear");
		const newYearNameInput = document.getElementById("newYearName");
		const defaultNamePreview = document.getElementById("defaultNewYearNamePreview");
		if (!newYearInput) {
			return;
		}

		const parsedYear = Number.parseInt(newYearInput.value, 10);
		if (!Number.isFinite(parsedYear) || parsedYear < 2000) {
			return;
		}

		const defaultLabel = `${parsedYear} Panther Shootout`;
		if (newYearNameInput) {
			newYearNameInput.setAttribute("placeholder", defaultLabel);
		}
		if (defaultNamePreview) {
			defaultNamePreview.textContent = defaultLabel;
		}
	}

	function handleUploadSubmit() {
		const submitBtn = document.getElementById("validateUploadBtn");
		const spinner = document.getElementById("uploadBtnSpinner");
		const icon = document.getElementById("uploadBtnIcon");
		if (submitBtn) {
			submitBtn.disabled = true;
		}
		if (spinner) {
			spinner.classList.remove("d-none");
		}
		if (icon) {
			icon.classList.add("d-none");
		}
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
