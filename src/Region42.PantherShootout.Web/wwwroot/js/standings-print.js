// Per-pool landscape print view interactions (print trigger, pool sheet filter, division switcher, local timestamps)
(function () {
	"use strict";

	const ALL_POOLS_VALUE = "all";
	const HIDDEN_CLASS = "d-none";
	const LAST_SHEET_CLASS = "is-last-sheet";

	function initialize() {
		const printBtn = document.getElementById("printActionBtn");
		if (printBtn) {
			printBtn.addEventListener("click", handlePrintClick);
		}

		const divisionSelect = document.getElementById("printDivisionSelect");
		if (divisionSelect) {
			divisionSelect.addEventListener("change", handleDivisionChange);
		}

		const filterButtons = document.querySelectorAll(".print-pool-filter-btn");
		filterButtons.forEach(function (btn) {
			btn.addEventListener("click", handlePoolFilterClick);
		});

		formatLocalTimestamps();
	}

	function handlePrintClick() {
		globalThis.print();
	}

	function handleDivisionChange(event) {
		const select = event.currentTarget;
		const divisionKey = select.value;
		const year = document.body.getAttribute("data-year") || "";
		if (year && divisionKey) {
			globalThis.location.href = `/standings/${encodeURIComponent(year)}/${encodeURIComponent(divisionKey)}/print`;
		}
	}

	function handlePoolFilterClick(event) {
		const clickedBtn = event.currentTarget;
		const selectedPool = clickedBtn.getAttribute("data-pool") || ALL_POOLS_VALUE;
		applyPoolFilter(selectedPool);
	}

	function applyPoolFilter(selectedPool) {
		const filterButtons = document.querySelectorAll(".print-pool-filter-btn");
		filterButtons.forEach(function (btn) {
			const btnPool = btn.getAttribute("data-pool") || ALL_POOLS_VALUE;
			const isActive = btnPool.toLowerCase() === selectedPool.toLowerCase();
			btn.classList.toggle("btn-dark", isActive);
			btn.classList.toggle("btn-outline-dark", !isActive);
			btn.setAttribute("aria-pressed", isActive ? "true" : "false");
		});

		const sheets = document.querySelectorAll(".print-pool-sheet");
		let visibleCount = 0;

		sheets.forEach(function (sheet) {
			const sheetPool = sheet.getAttribute("data-pool") || "";
			const shouldShow = selectedPool === ALL_POOLS_VALUE ||
				sheetPool.toLowerCase() === selectedPool.toLowerCase();
			sheet.classList.toggle(HIDDEN_CLASS, !shouldShow);
			if (shouldShow) {
				visibleCount++;
			}
		});

		syncLastVisibleSheet(sheets);
		updatePageCountLabel(visibleCount);
		updateDocumentTitle(selectedPool);
		syncUrlPoolQuery(selectedPool);
	}

	function syncLastVisibleSheet(sheets) {
		let lastVisible = null;
		sheets.forEach(function (sheet) {
			sheet.classList.remove(LAST_SHEET_CLASS);
			if (!sheet.classList.contains(HIDDEN_CLASS)) {
				lastVisible = sheet;
			}
		});

		if (lastVisible) {
			lastVisible.classList.add(LAST_SHEET_CLASS);
		}
	}

	function updatePageCountLabel(visibleCount) {
		const label = document.getElementById("visiblePageCountLabel");
		if (label) {
			label.textContent = `${visibleCount} ${visibleCount === 1 ? "page" : "pages"}`;
		}
	}

	function updateDocumentTitle(selectedPool) {
		const year = document.body.getAttribute("data-year") || "";
		const divisionName = document.body.getAttribute("data-division-name") || "";
		if (!divisionName || !year) {
			return;
		}

		if (selectedPool && selectedPool !== ALL_POOLS_VALUE) {
			document.title = `${divisionName} Pool ${selectedPool} — ${year} Panther Shootout`;
		} else {
			document.title = `${divisionName} — ${year} Panther Shootout`;
		}
	}

	function syncUrlPoolQuery(selectedPool) {
		if (!globalThis.history?.replaceState || !globalThis.location) {
			return;
		}

		const url = new URL(globalThis.location.href);
		if (!selectedPool || selectedPool === ALL_POOLS_VALUE) {
			url.searchParams.delete("pool");
		} else {
			url.searchParams.set("pool", selectedPool);
		}

		globalThis.history.replaceState(null, "", url.toString());
	}

	function formatLocalTimestamps() {
		const elements = document.querySelectorAll(".js-local-timestamp[data-utc-iso]");
		if (elements.length === 0 || !globalThis.Intl?.DateTimeFormat) {
			return;
		}

		const formatter = new Intl.DateTimeFormat(undefined, {
			weekday: "short",
			month: "short",
			day: "numeric",
			hour: "numeric",
			minute: "2-digit"
		});

		elements.forEach(function (el) {
			const iso = el.getAttribute("data-utc-iso");
			if (!iso) {
				return;
			}

			const parsed = new Date(iso);
			if (!Number.isNaN(parsed.getTime())) {
				el.textContent = `Updated ${formatter.format(parsed)}`;
			}
		});
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
