// Admin Team Editor filter navigation, live display-name preview, and Region 42 volunteer toggle
(function () {
	"use strict";

	const HOST_REGION_NUMBER = 42;

	function initialize() {
		const yearSelect = document.getElementById("adminTeamsYearSelect");
		if (yearSelect && yearSelect.tagName === "SELECT") {
			yearSelect.addEventListener("change", handleFilterChange);
		}

		const divisionSelect = document.getElementById("adminTeamsDivisionSelect");
		if (divisionSelect) {
			divisionSelect.addEventListener("change", handleFilterChange);
		}

		const teamRows = document.querySelectorAll(".admin-team-row");
		teamRows.forEach(bindTeamRow);
	}

	function handleFilterChange() {
		const yearEl = document.getElementById("adminTeamsYearSelect");
		const divisionEl = document.getElementById("adminTeamsDivisionSelect");
		const yearId = yearEl ? yearEl.value : "";
		const divisionKey = divisionEl
			? divisionEl.value
			: (yearEl ? yearEl.getAttribute("data-division-key") || "" : "");

		const params = new URLSearchParams();
		if (yearId) {
			params.set("yearId", yearId);
		}
		if (divisionKey) {
			params.set("division", divisionKey);
		}

		const query = params.toString();
		globalThis.location.href = query ? `/Admin/Teams?${query}` : "/Admin/Teams";
	}

	function bindTeamRow(row) {
		const regionSelect = row.querySelector(".team-region-select");
		const coachInput = row.querySelector(".team-coach-input");

		if (regionSelect) {
			regionSelect.addEventListener("change", function () {
				syncVolunteerToggle(row, regionSelect);
				updateRowPreview(row, regionSelect, coachInput);
			});
		}

		if (coachInput) {
			coachInput.addEventListener("input", function () {
				updateRowPreview(row, regionSelect, coachInput);
			});
		}
	}

	function syncVolunteerToggle(row, regionSelect) {
		const regionNum = Number.parseInt(regionSelect.value, 10);
		const isHostRegion = regionNum === HOST_REGION_NUMBER;
		const switchWrap = row.querySelector(".volunteer-deduction-wrap");
		const checkbox = row.querySelector(".volunteer-deduction-checkbox");
		const nonHostNote = row.querySelector(".volunteer-nonhost-note");

		if (checkbox) {
			if (!isHostRegion) {
				checkbox.checked = false;
			}
			checkbox.disabled = !isHostRegion;
		}

		if (switchWrap) {
			switchWrap.classList.toggle("d-none", !isHostRegion);
		}

		if (nonHostNote) {
			nonHostNote.classList.toggle("d-none", isHostRegion);
		}
	}

	function updateRowPreview(row, regionSelect, coachInput) {
		const previewEl = row.querySelector(".team-preview-text");
		const previewContainer = row.querySelector(".team-display-preview");
		if (!previewEl) {
			return;
		}

		const coach = coachInput ? coachInput.value.trim() : "";
		let regionName = "";
		if (regionSelect && regionSelect.selectedIndex >= 0) {
			const selectedOption = regionSelect.options[regionSelect.selectedIndex];
			regionName = selectedOption ? (selectedOption.getAttribute("data-region-name") || "") : "";
		}

		const formatted = coach
			? (regionName ? `${regionName}: ${coach}` : coach)
			: (regionName || "—");

		previewEl.textContent = formatted;
		if (previewContainer) {
			previewContainer.setAttribute("title", formatted);
		}
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
