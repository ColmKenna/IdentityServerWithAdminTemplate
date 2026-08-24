(function (global, factory) {
  typeof exports === 'object' && typeof module !== 'undefined' ? factory(exports) :
  typeof define === 'function' && define.amd ? define(['exports'], factory) :
  (global = typeof globalThis !== 'undefined' ? globalThis : global || self, factory(global.WebComponentLibrary = {}));
})(this, (function (exports) { 'use strict';

  // global utilities (no @media here) — kept small and shared
  const baseCSS = `
  .small-screen-only { display: none; }
  .align-left { text-align: left; }
  .align-center { text-align: center; }
  .align-right { text-align: right; }
`;
  if ('adoptedStyleSheets' in document &&
      'replaceSync' in CSSStyleSheet.prototype) {
      const sheet = new CSSStyleSheet();
      sheet.replaceSync(baseCSS);
      document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
  }
  else {
      const styleFallback = document.createElement('style');
      styleFallback.textContent = baseCSS;
      document.head.appendChild(styleFallback);
  }
  class CkResponsiveTable extends HTMLElement {
      constructor() {
          super(...arguments);
          this.__initialized = false;
      }
      connectedCallback() {
          if (this.__initialized)
              return;
          this.__initialized = true;
          // allow the user to set the mobile breakpoint via attribute `mobile-breakpoint`
          // default remains 600px for backwards compatibility
          const breakpoint = (this.getAttribute('mobile-breakpoint') || '600px').trim();
          // create table element and copy attributes
          const table = document.createElement('table');
          for (const { name, value } of Array.from(this.attributes)) {
              table.setAttribute(name, value ?? '');
          }
          // give this table a unique scoped class so we can inject per-table CSS
          CkResponsiveTable._counter = (CkResponsiveTable._counter || 0) + 1;
          const uid = CkResponsiveTable._counter;
          const scopeClass = `responsive-table-${uid}`;
          table.classList.add('responsive-table', scopeClass);
          // generate and inject scoped mobile CSS that uses the user-provided breakpoint
          const perTableCSS = `
      @media (max-width: ${breakpoint}) {
        /* mobile-align utility classes (scoped) */
        .${scopeClass} .mobile-align-left { text-align: left; }
        .${scopeClass} .mobile-align-center { text-align: center; }
        .${scopeClass} .mobile-align-right { text-align: right; }

        .${scopeClass} td {
          display: flex;
          flex-direction: column;
          border: none !important;
        }
        .${scopeClass}.table-bordered td,
        .${scopeClass}.table-bordered th {
          border: none !important;
        }
        .${scopeClass} tr {
          padding: 0.25rem;
          display: flex;
          flex-direction: column;
          border-bottom: 1px solid var(--colour-border, #DDDDDD);
        }
        .${scopeClass} tr:last-child {
          border-bottom: none;
        }
        .${scopeClass} thead { display: none; }
        .${scopeClass} td span.small-screen-only {
          display: block;
          font-weight: bold;
          margin-right: 5px;
        }

        /* mobile-direction class variants (scoped) */
        .${scopeClass} td.mobile-direction-row { flex-direction: row; }
        .${scopeClass} td.mobile-direction-column { flex-direction: column; }
        .${scopeClass} td.mobile-direction-row-reverse { flex-direction: row-reverse; }
        .${scopeClass} td.mobile-direction-column-reverse { flex-direction: column-reverse; }

        /* mobile-justify class variants (scoped) */
        .${scopeClass} td.mobile-justify-flex-start { justify-content: flex-start; }
        .${scopeClass} td.mobile-justify-center { justify-content: center; }
        .${scopeClass} td.mobile-justify-flex-end { justify-content: flex-end; }
        .${scopeClass} td.mobile-justify-space-between { justify-content: space-between; }
        .${scopeClass} td.mobile-justify-space-around { justify-content: space-around; }
        .${scopeClass} td.mobile-justify-space-evenly { justify-content: space-evenly; }
      }
    `;
          if ('adoptedStyleSheets' in document &&
              'replaceSync' in CSSStyleSheet.prototype) {
              const sheet = new CSSStyleSheet();
              sheet.replaceSync(perTableCSS);
              document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
          }
          else {
              const styleEl = document.createElement('style');
              styleEl.textContent = perTableCSS;
              document.head.appendChild(styleEl);
          }
          // Transform custom thead/col elements into real thead/tr/th
          const ckThead = this.querySelector('ck-responsive-thead');
          let headerValues = [];
          if (ckThead) {
              headerValues = Array.from(ckThead.querySelectorAll('ck-responsive-col-head')).map(col => {
                  const span = document.createElement('span');
                  span.classList.add('small-screen-only');
                  span.innerHTML = col.innerHTML;
                  const align = (col.getAttribute('align') || '').toLowerCase();
                  const alignClass = align ? `align-${align}` : null;
                  const mobileAlign = (col.getAttribute('mobile-align') || '').toLowerCase();
                  const mobileAlignClass = mobileAlign
                      ? `mobile-align-${mobileAlign}`
                      : null;
                  const noLabel = (col.getAttribute('mobile-no-label') || '').toLowerCase() === 'true';
                  return { label: span, alignClass, mobileAlignClass, noLabel };
              });
              const thead = document.createElement('thead');
              if (ckThead.getAttribute('class'))
                  thead.setAttribute('class', ckThead.getAttribute('class'));
              const tr = document.createElement('tr');
              const colHeads = Array.from(ckThead.querySelectorAll('ck-responsive-col-head'));
              for (let i = 0; i < colHeads.length; i++) {
                  const col = colHeads[i];
                  const th = document.createElement('th');
                  if (col.getAttribute('class'))
                      th.setAttribute('class', col.getAttribute('class'));
                  th.textContent = col.textContent;
                  const alignClass = headerValues[i] && headerValues[i].alignClass;
                  if (alignClass)
                      th.classList.add(alignClass);
                  tr.appendChild(th);
              }
              thead.appendChild(tr);
              table.appendChild(thead);
          }
          // Transform custom tbody/rows/cols into real tbody/tr/td
          const ckTbody = this.querySelector('ck-responsive-tbody');
          if (ckTbody) {
              const tbody = document.createElement('tbody');
              if (ckTbody.getAttribute('class'))
                  tbody.setAttribute('class', ckTbody.getAttribute('class'));
              for (const row of Array.from(ckTbody.querySelectorAll('ck-responsive-row'))) {
                  const tr = document.createElement('tr');
                  if (row.getAttribute('class'))
                      tr.setAttribute('class', row.getAttribute('class'));
                  for (const col of Array.from(row.querySelectorAll('ck-responsive-col'))) {
                      const td = document.createElement('td');
                      const colIndex = Array.from(row.children).indexOf(col);
                      if (col.getAttribute('class'))
                          td.setAttribute('class', col.getAttribute('class'));
                      const correspondingHeader = headerValues[colIndex];
                      if (correspondingHeader) {
                          // only add the small-screen label span if mobile-no-label is not set
                          if (!correspondingHeader.noLabel) {
                              td.appendChild(correspondingHeader.label.cloneNode(true));
                          }
                          if (correspondingHeader.alignClass) {
                              td.classList.add(correspondingHeader.alignClass);
                          }
                          if (correspondingHeader.mobileAlignClass) {
                              td.classList.add(correspondingHeader.mobileAlignClass);
                          }
                      }
                      // apply mobile-direction from ck-col if present (e.g. mobile-direction="row")
                      const mobileDirection = (col.getAttribute('mobile-direction') || '')
                          .toLowerCase()
                          .trim();
                      if (mobileDirection) {
                          const safe = mobileDirection.replace(/\s+/g, '-'); // e.g. "row reverse" -> "row-reverse"
                          td.classList.add(`mobile-direction-${safe}`);
                      }
                      // apply mobile-justify from ck-col if present (e.g. mobile-justify="center" or "space-between")
                      const mobileJustify = (col.getAttribute('mobile-justify') || '')
                          .toLowerCase()
                          .trim();
                      if (mobileJustify) {
                          const safeJustify = mobileJustify.replace(/\s+/g, '-'); // e.g. "space between" -> "space-between"
                          td.classList.add(`mobile-justify-${safeJustify}`);
                      }
                      // create an element from col's innerHTML
                      const colContent = document.createElement('span');
                      colContent.classList.add('content');
                      colContent.innerHTML = col.innerHTML;
                      td.appendChild(colContent);
                      tr.appendChild(td);
                  }
                  tbody.appendChild(tr);
              }
              table.appendChild(tbody);
          }
          // Replace custom tag with the table element
          this.replaceWith(table);
      }
  }
  CkResponsiveTable._counter = 0;
  customElements.define('ck-responsive-table', CkResponsiveTable);
  // Define placeholder elements to avoid "unknown element" warnings
  const definePlaceholder = (name) => {
      if (!customElements.get(name)) {
          customElements.define(name, class extends HTMLElement {
          });
      }
  };
  definePlaceholder('ck-responsive-thead');
  definePlaceholder('ck-responsive-col-head');
  definePlaceholder('ck-responsive-tbody');
  definePlaceholder('ck-responsive-row');
  definePlaceholder('ck-responsive-col');

  exports.CkResponsiveTable = CkResponsiveTable;

}));
//# sourceMappingURL=index.js.map
