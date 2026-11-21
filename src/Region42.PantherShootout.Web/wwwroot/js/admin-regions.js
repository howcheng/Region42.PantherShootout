// AYSO Regions admin search, inline edit population, and delete confirmation
(function () {
	"use strict";

	function initialize() {
		const searchInput = document.getElementById("regionSearchInput");
		if (searchInput) {
			searchInput.addEventListener("input", filterRegions);
		}

		document.addEventListener("click", handleDocumentClick);

		const cancelEditBtn = document.getElementById("cancelRegionEditBtn");
		if (cancelEditBtn) {
			cancelEditBtn.addEventListener("click", resetRegionForm);
		}

		const deleteForms = document.querySelectorAll(".delete-region-form");
		deleteForms.forEach(function (form) {
			form.addEventListener("submit", confirmRegionDelete);
		});
	}

	function filterRegions() {
		const searchInput = document.getElementById("regionSearchInput");
		const query = (searchInput ? searchInput.value : "").trim().toLowerCase();
		const rows = document.querySelectorAll(".region-item-row");
		let visibleCount = 0;

		rows.forEach(function (row) {
			const searchText = (row.getAttribute("data-search") || "").toLowerCase();
			const matches = query === "" || searchText.includes(query);
			row.classList.toggle("d-none", !matches);
			if (matches) {
				visibleCount++;
			}
		});

		const emptyNotice = document.getElementById("regionSearchEmpty");
		if (emptyNotice) {
			emptyNotice.classList.toggle("d-none", visibleCount > 0);
		}
	}

	function handleDocumentClick(event) {
		const editBtn = event.target.closest(".edit-region-btn");
		if (!editBtn) {
			return;
		}

		event.preventDefault();
		populateEditForm(
			editBtn.getAttribute("data-region-id") || "",
			editBtn.getAttribute("data-region-number") || "",
			editBtn.getAttribute("data-region-name") || ""
		);
	}

	function populateEditForm(id, regionNumber, name) {
		const idInput = document.getElementById("EditId");
		const numInput = document.getElementById("RegionNumber");
		const nameInput = document.getElementById("Name");
		const formTitle = document.getElementById("regionFormTitle");
		const submitText = document.getElementById("regionSubmitBtnText");
		const cancelBtn = document.getElementById("cancelRegionEditBtn");

		if (idInput) {
			idInput.value = id;
		}
		if (numInput) {
			numInput.value = regionNumber;
		}
		if (nameInput) {
			nameInput.value = name;
			nameInput.focus();
		}
		if (formTitle) {
			formTitle.textContent = `Edit AYSO Region ${regionNumber}`;
		}
		if (submitText) {
			submitText.textContent = "Update Region";
		}
		if (cancelBtn) {
			cancelBtn.classList.remove("d-none");
		}
	}

	function resetRegionForm() {
		const idInput = document.getElementById("EditId");
		const numInput = document.getElementById("RegionNumber");
		const nameInput = document.getElementById("Name");
		const formTitle = document.getElementById("regionFormTitle");
		const submitText = document.getElementById("regionSubmitBtnText");
		const cancelBtn = document.getElementById("cancelRegionEditBtn");

		if (idInput) {
			idInput.value = "";
		}
		if (numInput) {
			numInput.value = "";
		}
		if (nameInput) {
			nameInput.value = "";
		}
		if (formTitle) {
			formTitle.textContent = "Add or Update AYSO Region";
		}
		if (submitText) {
			submitText.textContent = "Save Region";
		}
		if (cancelBtn) {
			cancelBtn.classList.add("d-none");
		}
	}

	function confirmRegionDelete(event) {
		const form = event.currentTarget;
		const label = form.getAttribute("data-region-label") || "this region";
		if (!globalThis.confirm(`Delete ${label}?`)) {
			event.preventDefault();
		}
	}

	if (document.readyState === "loading") {
		document.addEventListener("DOMContentLoaded", initialize);
	} else {
		initialize();
	}
})();
