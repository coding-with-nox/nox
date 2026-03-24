window.noxTheme = {
    init: function () {
        const saved = localStorage.getItem('nox-theme') || 'light';
        document.documentElement.setAttribute('data-theme', saved);
        return saved;
    },
    set: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('nox-theme', theme);
    }
};

window.noxLang = {
    init: function () {
        return localStorage.getItem('nox-lang') || 'it';
    },
    set: function (lang) {
        localStorage.setItem('nox-lang', lang);
    }
};
