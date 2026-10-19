// Applies the saved look before the first paint, so people who chose dark mode never see a white flash.
// Keys, attributes and values must match CHOICES in src/lib/appearance.ts (a default has no attribute).
(function () {
  var CHOICES = [
    ['accent', 'data-accent', 'steel|azure|teal|green|indigo|violet|rose|amber|slate', 'steel'],
    ['font', 'data-font', 'inter|plex|sarabun|system', 'inter'],
    ['base', 'data-base', 'slate|gray|zinc|neutral|stone', 'slate'],
    ['radius', 'data-radius', 'none|sm|md|lg|xl', 'sm'],
    ['nav', 'data-nav', 'side|rail|top', 'side'],
    ['container', 'data-container', 'narrow|standard|wide|fluid', 'standard'],
    ['contrast', 'data-contrast', 'standard|high', 'standard'],
    ['motion', 'data-motion', 'auto|reduce|full', 'auto'],
    ['size', 'data-size', 's|m|l', 'm']
  ];
  try {
    var root = document.documentElement;
    for (var i = 0; i < CHOICES.length; i++) {
      var c = CHOICES[i];
      var v = localStorage.getItem('kpi.' + c[0]);
      if (v && v !== c[3] && new RegExp('^(' + c[2] + ')$').test(v)) root.setAttribute(c[1], v);
    }
    var pref = localStorage.getItem('kpi.theme');
    if (pref === 'dark' || (pref === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
      root.setAttribute('data-theme', 'dark');
    }
  } catch (e) { /* storage blocked: the defaults */ }
})();
