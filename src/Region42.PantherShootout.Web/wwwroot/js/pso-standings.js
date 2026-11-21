// Penalty Shootout (PSO) page year switcher
(function () {
	"use strict";

	function initialize() {
		const yearSelect = document.getElementById("psoYearSelect");
		if (yearSelect) {
			yearSelect.addEventListener("change", handleYearSwitch);
		}
	}

	function handleYearSwitch(event) {
		const select = event.currentTarget;
		const year = select.value;
		const divisionKey = select.getAttribute("data-division-key") || "";
		if (!year) {
			return;
		}

		let url = `/shootout/${encodeURIComponent(year)}`;
		if (divisionKey) {
			url += `?division=${encodeURIComponent(divisionKey)}`;
		}
		globalThis.location.href = url;
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
