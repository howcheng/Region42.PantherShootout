// Division Standings page interactions (year switcher, URL hash mobile tabs, pool schedule filter, team stats modal)
(function () {
	"use strict";

	function initialize() {
		const yearSelect = document.getElementById("divisionYearSelect");
		if (yearSelect) {
			yearSelect.addEventListener("change", handleYearSwitch);
		}

		initializeMobileTabHashSync();
		initializePoolScheduleFilter();
		document.addEventListener("click", handleTeamStatsClick);
	}

	function handleYearSwitch(event) {
		const select = event.currentTarget;
		const year = select.value;
		const divisionKey = select.getAttribute("data-division-key") || "";
		if (year && divisionKey) {
			globalThis.location.href = `/standings/${encodeURIComponent(year)}/${encodeURIComponent(divisionKey)}`;
		} else if (year) {
			globalThis.location.href = `/standings/${encodeURIComponent(year)}`;
		}
	}

	function initializeMobileTabHashSync() {
		const tabButtons = document.querySelectorAll("#divisionMobileTabs button[data-bs-toggle='pill']");
		if (tabButtons.length === 0) {
			return;
		}

		const currentHash = globalThis.location.hash;
		if (currentHash) {
			const matchingBtn = document.querySelector(`#divisionMobileTabs button[data-bs-target='${CSS.escape(currentHash)}']`);
			if (matchingBtn && globalThis.bootstrap?.Tab) {
				globalThis.bootstrap.Tab.getOrCreateInstance(matchingBtn).show();
			}
		}

		tabButtons.forEach(function (btn) {
			btn.addEventListener("shown.bs.tab", function (ev) {
				const target = ev.target.getAttribute("data-bs-target");
				if (target && globalThis.history?.replaceState) {
					globalThis.history.replaceState(null, "", target);
				}
			});
		});
	}

	function initializePoolScheduleFilter() {
		const filterButtons = document.querySelectorAll(".schedule-pool-filter-btn");
		if (filterButtons.length === 0) {
			return;
		}

		filterButtons.forEach(function (btn) {
			btn.addEventListener("click", function () {
				const selectedPool = btn.getAttribute("data-pool") || "all";

				filterButtons.forEach(function (other) {
					const active = other === btn;
					other.classList.toggle("btn-primary", active);
					other.classList.toggle("btn-outline-secondary", !active);
					other.setAttribute("aria-pressed", active ? "true" : "false");
				});

				filterScheduleCards(selectedPool);
			});
		});
	}

	function filterScheduleCards(pool) {
		const cards = document.querySelectorAll(".schedule-match-card[data-pool]");
		cards.forEach(function (card) {
			const cardPool = card.getAttribute("data-pool") || "";
			const show = pool === "all" || cardPool === pool;
			card.classList.toggle("d-none", !show);
		});

		const roundGroups = document.querySelectorAll(".schedule-round-group");
		roundGroups.forEach(function (group) {
			const anyVisible = group.querySelectorAll(".schedule-match-card:not(.d-none)").length > 0;
			group.classList.toggle("d-none", !anyVisible);
		});
	}

	function handleTeamStatsClick(event) {
		const btn = event.target.closest(".team-stats-modal-btn");
		if (!btn) {
			return;
		}

		event.preventDefault();
		populateAndShowStatsModal(btn);
	}

	function populateAndShowStatsModal(btn) {
		const modalEl = document.getElementById("teamStatsDetailModal");
		if (!modalEl || !globalThis.bootstrap?.Modal) {
			return;
		}

		setText("statsModalTeamTitle", btn.getAttribute("data-team-name") || "");
		setText("statsModalRecord", btn.getAttribute("data-record") || "");
		setText("statsModalPoints", btn.getAttribute("data-points") || "0");
		setText("statsModalGf", btn.getAttribute("data-gf") || "0");
		setText("statsModalGa", btn.getAttribute("data-ga") || "0");
		setText("statsModalGd", btn.getAttribute("data-gd") || "0");
		setText("statsModalYc", btn.getAttribute("data-yc") || "0");
		setText("statsModalRc", btn.getAttribute("data-rc") || "0");

		const forfeitNotice = document.getElementById("statsModalForfeitNotice");
		if (forfeitNotice) {
			const hasForfeit = btn.getAttribute("data-forfeit") === "true";
			forfeitNotice.classList.toggle("d-none", !hasForfeit);
		}

		globalThis.bootstrap.Modal.getOrCreateInstance(modalEl).show();
	}

	function setText(id, value) {
		const el = document.getElementById(id);
		if (el) {
			el.textContent = value;
		}
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
