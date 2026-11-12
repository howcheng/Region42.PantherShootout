// Dark mode / light mode theme toggle
(function () {
	const THEME_COOKIE_NAME = "region42_theme";
	const THEME_COOKIE_DAYS = 400; // ~13 months; refreshed on the server on every request
	const MILLISECONDS_PER_DAY = 24 * 60 * 60 * 1000;

	document.addEventListener("DOMContentLoaded", function () {
		const toggle = document.getElementById("theme-toggle");
		if (!toggle) {
			return;
		}

		applyTheme(currentTheme());

		toggle.addEventListener("click", function () {
			const newTheme = currentTheme() === "dark" ? "light" : "dark";
			applyTheme(newTheme);
			setCookie(THEME_COOKIE_NAME, newTheme, THEME_COOKIE_DAYS);
		});
	});

	function currentTheme() {
		return document.documentElement.getAttribute("data-bs-theme") === "dark" ? "dark" : "light";
	}

	function applyTheme(theme) {
		document.documentElement.setAttribute("data-bs-theme", theme);
		const toggle = document.getElementById("theme-toggle");
		if (toggle) {
			const isDark = theme === "dark";
			toggle.innerHTML = isDark
				? '<i class="bi bi-sun-fill" aria-hidden="true"></i>'
				: '<i class="bi bi-moon-fill" aria-hidden="true"></i>';
			const label = isDark ? "Switch to light mode" : "Switch to dark mode";
			toggle.setAttribute("title", label);
			toggle.setAttribute("aria-label", label);
		}
	}

	function setCookie(name, value, days) {
		const expires = new Date();
		expires.setTime(expires.getTime() + days * MILLISECONDS_PER_DAY);
		const secureFlag = globalThis.location?.protocol === "https:" ? "; Secure" : "";
		document.cookie = `${name}=${encodeURIComponent(value)}; expires=${expires.toUTCString()}; path=/; SameSite=Lax${secureFlag}`;
	}
})();
