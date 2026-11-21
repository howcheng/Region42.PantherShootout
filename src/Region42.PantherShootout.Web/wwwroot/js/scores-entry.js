// Unified Division Score Entry interactions & validation
(function () {
	"use strict";

	let isDirty = false;

	function initialize() {
		const yearSelect = document.getElementById("scoresYearSelect");
		const divisionSelect = document.getElementById("scoresDivisionSelect");
		const roundSelect = document.getElementById("scoresRoundSelect");

		if (yearSelect) {
			yearSelect.addEventListener("change", reloadFilterSelection);
		}
		if (divisionSelect) {
			divisionSelect.addEventListener("change", reloadFilterSelection);
		}
		if (roundSelect) {
			roundSelect.addEventListener("change", reloadFilterSelection);
		}

		const tabButtons = document.querySelectorAll("#scoresSectionTabs button[data-tab-name]");
		tabButtons.forEach(function (btn) {
			btn.addEventListener("shown.bs.tab", function (ev) {
				const tabName = ev.target.getAttribute("data-tab-name") || "pool";
				const activeTabInput = document.getElementById("activeTabInput");
				if (activeTabInput) {
					activeTabInput.value = tabName;
				}
			});
		});

		const batchForm = document.getElementById("divisionBatchScoresForm");
		if (batchForm) {
			batchForm.addEventListener("input", markDirty);
			batchForm.addEventListener("change", markDirty);
			batchForm.addEventListener("submit", validateBatchScores);
		}

		const deleteKftmForms = document.querySelectorAll(".remove-kftm-form");
		deleteKftmForms.forEach(function (form) {
			form.addEventListener("submit", function (event) {
				if (!globalThis.confirm("Remove this KFTM tiebreaker override?")) {
					event.preventDefault();
				}
			});
		});
	}

	function reloadFilterSelection() {
		const yearSelect = document.getElementById("scoresYearSelect");
		const divisionSelect = document.getElementById("scoresDivisionSelect");
		const roundSelect = document.getElementById("scoresRoundSelect");
		const activeTabInput = document.getElementById("activeTabInput");

		const params = [];
		if (yearSelect && yearSelect.value) {
			params.push(`year=${encodeURIComponent(yearSelect.value)}`);
		}
		if (divisionSelect && divisionSelect.value) {
			params.push(`division=${encodeURIComponent(divisionSelect.value)}`);
		}
		if (roundSelect && roundSelect.value) {
			params.push(`round=${encodeURIComponent(roundSelect.value)}`);
		}
		if (activeTabInput && activeTabInput.value) {
			params.push(`tab=${encodeURIComponent(activeTabInput.value)}`);
		}

		globalThis.location.href = params.length > 0 ? `/Scores?${params.join("&")}` : "/Scores";
	}

	function markDirty() {
		if (isDirty) {
			return;
		}

		isDirty = true;
		const badge = document.getElementById("unsavedChangesBadge");
		if (badge) {
			badge.classList.remove("d-none");
		}
	}

	function validateBatchScores(event) {
		const errors = [];

		validateGoalPairs(".pool-score-card", "Pool Play", true, errors);
		validateGoalPairs(".champ-score-card", "Bracket", true, errors);
		validatePsoPairs(".pso-pair-group", errors);

		if (errors.length > 0) {
			event.preventDefault();
			globalThis.alert("Please fix the following before saving:\n\n• " + errors.join("\n• "));
			return false;
		}

		return true;
	}

	function validateGoalPairs(selector, sectionLabel, checkForfeit, errors) {
		const cards = document.querySelectorAll(selector);
		cards.forEach(function (card) {
			const label = card.getAttribute("data-match-label") || sectionLabel;
			const homeInput = card.querySelector(".home-goals-input");
			const awayInput = card.querySelector(".away-goals-input");
			if (!homeInput || !awayInput || homeInput.disabled) {
				return;
			}

			const homeRaw = homeInput.value.trim();
			const awayRaw = awayInput.value.trim();
			const hasHome = homeRaw !== "";
			const hasAway = awayRaw !== "";

			if (hasHome !== hasAway) {
				errors.push(`${label}: Enter both Home and Away goals (or leave both blank).`);
				return;
			}

			if (hasHome && hasAway) {
				const h = Number.parseInt(homeRaw, 10);
				const a = Number.parseInt(awayRaw, 10);
				if (!Number.isFinite(h) || !Number.isFinite(a) || h < 0 || a < 0) {
					errors.push(`${label}: Goals cannot be negative.`);
					return;
				}

				if (checkForfeit) {
					const forfeitBox = card.querySelector(".forfeit-checkbox");
					if (forfeitBox && forfeitBox.checked) {
						const validForfeit = (h === 1 && a === 0) || (h === 0 && a === 1);
						if (!validForfeit) {
							errors.push(`${label}: Forfeit games must be scored 1-0 or 0-1.`);
						}
					}
				}
			} else if (checkForfeit) {
				const forfeitBox = card.querySelector(".forfeit-checkbox");
				if (forfeitBox && forfeitBox.checked) {
					errors.push(`${label}: Checked Forfeit requires a 1-0 or 0-1 score.`);
				}
			}

			card.querySelectorAll(".card-count-input").forEach(function (cardInput) {
				const raw = cardInput.value.trim();
				if (raw !== "") {
					const c = Number.parseInt(raw, 10);
					if (!Number.isFinite(c) || c < 0) {
						errors.push(`${label}: Yellow/Red card counts cannot be negative.`);
					}
				}
			});
		});
	}

	function validatePsoPairs(selector, errors) {
		const groups = document.querySelectorAll(selector);
		groups.forEach(function (group) {
			const label = group.getAttribute("data-pso-label") || "PSO Shootout";
			const homePk = group.querySelector(".home-pk-input");
			const awayPk = group.querySelector(".away-pk-input");
			if (!homePk || !awayPk || homePk.disabled) {
				return;
			}

			const homeRaw = homePk.value.trim();
			const awayRaw = awayPk.value.trim();
			const hasHome = homeRaw !== "";
			const hasAway = awayRaw !== "";

			if (hasHome !== hasAway) {
				errors.push(`${label}: Enter both Home PK and Away PK goals (or leave both blank).`);
				return;
			}

			if (hasHome && hasAway) {
				const h = Number.parseInt(homeRaw, 10);
				const a = Number.parseInt(awayRaw, 10);
				if (!Number.isFinite(h) || !Number.isFinite(a) || h < 0 || a < 0) {
					errors.push(`${label}: PK goals cannot be negative.`);
				}
			}
		});
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
