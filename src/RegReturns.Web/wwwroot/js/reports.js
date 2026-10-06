// Draws the reports dashboard's charts with Chart.js. Each canvas carries its data in a data-chart attribute (JSON
// written by ReportsViewModel), and every chart's figures are also on the page as text, so the page reads the same
// without this script.
(() => {
  'use strict';

  if (typeof Chart === 'undefined') {
    return;
  }

  const palette = ['#0d6efd', '#dc3545', '#fd7e14', '#198754', '#6f42c1', '#20c997', '#6c757d'];

  const findingsChart = (canvas, spec) => new Chart(canvas, {
    type: 'bar',
    data: {
      labels: spec.labels,
      datasets: spec.series.map((series, i) => ({
        label: series.label,
        data: series.values,
        backgroundColor: palette[i % palette.length],
        stack: 'findings',
      })),
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        x: { stacked: true },
        y: { stacked: true, beginAtZero: true, ticks: { precision: 0 }, title: { display: true, text: 'Findings' } },
      },
      plugins: { legend: { position: 'bottom' } },
    },
  });

  const sparkline = (canvas, spec) => new Chart(canvas, {
    type: 'line',
    data: {
      labels: spec.labels,
      datasets: [{
        data: spec.values,
        borderColor: palette[0],
        borderWidth: 2,
        pointRadius: 0,
        pointHitRadius: 6,
        spanGaps: true,
        // Monotone curves never overshoot, so the line never shows a value no bank reported.
        cubicInterpolationMode: 'monotone',
      }],
    },
    options: {
      responsive: false,
      animation: false,
      scales: { x: { display: false }, y: { display: false } },
      plugins: { legend: { display: false } },
    },
  });

  for (const canvas of document.querySelectorAll('canvas[data-chart]')) {
    const spec = JSON.parse(canvas.dataset.chart);
    if (spec.kind === 'findings') {
      findingsChart(canvas, spec);
    } else if (spec.kind === 'sparkline') {
      sparkline(canvas, spec);
    }
  }
})();
