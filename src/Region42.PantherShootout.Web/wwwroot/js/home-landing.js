// Tournament Hub landing page year switcher
(function () {
	"use strict";

	function initialize() {
		const yearSelect = document.getElementById("landingYearSelect");
		if (yearSelect) {
			yearSelect.addEventListener("change", handleYearChange);
		}
	}

	function handleYearChange(event) {
		const year = event.currentTarget.value;
		if (year) {
			globalThis.location.href = `/?year=${encodeURIComponent(year)}`;
		} else {
			globalThis.location.href = "/";
		}
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
