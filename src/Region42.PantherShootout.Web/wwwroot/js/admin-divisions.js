// Admin Division Bulletins year filter
(function () {
	"use strict";

	function initialize() {
		const yearSelect = document.getElementById("bulletinYearSelect");
		if (yearSelect) {
			yearSelect.addEventListener("change", function (ev) {
				const yearId = ev.currentTarget.value;
				globalThis.location.href = yearId
					? `/Admin/Divisions?yearId=${encodeURIComponent(yearId)}`
					: "/Admin/Divisions";
			});
		}
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
