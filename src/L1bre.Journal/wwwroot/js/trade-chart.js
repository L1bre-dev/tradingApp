// <trade-chart data-chart="{...}">: draws a trade's 54 candles with TradingView Lightweight Charts.
//
// The pages are rendered on the server (no Blazor interactivity), and Blazor's enhanced navigation swaps
// page content without running page scripts. A custom element avoids both problems: the browser calls
// connectedCallback whenever the element appears, and attributeChangedCallback when navigation only
// changes its data. The chart lives in a shadow root, which Blazor's DOM updates never touch.
//
// data-chart = { digits, direction: "Buy"|"Sell", entry, sl, tp, fill (unix seconds), timeframe,
//                charts: { "M15": [[openTime, open, high, low, close, isCut], ...], ... } }
// Buttons with data-chart-tf="M15" and a <select data-chart-tf-select> elsewhere on the page switch the
// timeframe instantly (all timeframes are already in the page) and stay in step with each other.
(() => {
    const colors = { up: '#26a69a', down: '#ef5350', entry: '#1e63d6', sl: '#d32f2f', tp: '#2e7d32' };

    class TradeChart extends HTMLElement {
        static observedAttributes = ['data-chart'];

        connectedCallback() { this.render(); }
        disconnectedCallback() { this.destroy(); }
        attributeChangedCallback() { if (this.isConnected) this.render(); }

        destroy() {
            this.chart?.remove();
            this.chart = null;
        }

        render() {
            this.destroy();
            let data;
            try { data = JSON.parse(this.dataset.chart || '{}'); } catch { return; }
            if (!data.charts || !window.LightweightCharts) return;
            this.data = data;

            const root = this.shadowRoot ?? this.attachShadow({ mode: 'open' });
            root.innerHTML = `
                <style>
                    :host { display: block; position: relative; }
                    .chart { position: absolute; inset: 0; }
                    .legend { position: absolute; top: 6px; left: 8px; right: 80px; z-index: 3; font: 12px system-ui, sans-serif;
                              color: #333; pointer-events: none; text-shadow: 0 0 3px #fff; }
                </style>
                <div class="chart"></div><div class="legend"></div>`;
            this.legend = root.querySelector('.legend');

            const LC = window.LightweightCharts;
            const minMove = Math.pow(10, -data.digits);
            this.chart = LC.createChart(root.querySelector('.chart'), {
                autoSize: true,
                layout: { attributionLogo: true },
                timeScale: { timeVisible: true, secondsVisible: false, rightOffset: 3 },
                // Times are shown as they are stored: UTC, like the rest of the journal.
                // A fixed locale: the browser's own can be one the chart library rejects (e.g. "en-US@posix").
                localization: { locale: 'en-GB', dateFormat: 'yyyy-MM-dd', priceFormatter: p => p.toFixed(data.digits) },
            });
            const levels = [data.entry, data.sl, data.tp].filter(p => typeof p === 'number');
            this.series = this.chart.addSeries(LC.CandlestickSeries, {
                upColor: colors.up, downColor: colors.down, borderVisible: false,
                wickUpColor: colors.up, wickDownColor: colors.down,
                priceFormat: { type: 'price', precision: data.digits, minMove },
                // Keep the SL and TP inside the visible price range, even when the candles never reach them.
                autoscaleInfoProvider: original => {
                    const res = original();
                    if (res?.priceRange) {
                        res.priceRange.minValue = Math.min(res.priceRange.minValue, ...levels);
                        res.priceRange.maxValue = Math.max(res.priceRange.maxValue, ...levels);
                    }
                    return res;
                },
            });
            const line = (price, color, title, style) =>
                typeof price === 'number' && this.series.createPriceLine({ price, color, title, lineWidth: 1, lineStyle: style, axisLabelVisible: true });
            line(data.entry, colors.entry, data.direction, LC.LineStyle.Dotted);
            line(data.sl, colors.sl, 'SL', LC.LineStyle.Dashed);
            line(data.tp, colors.tp, 'TP', LC.LineStyle.Dashed);
            this.markers = LC.createSeriesMarkers(this.series, []);

            this.chart.subscribeCrosshairMove(param => {
                const bar = param.time ? param.seriesData.get(this.series) : null;
                this.showLegend(bar ?? this.lastBar);
            });

            const first = data.charts[data.timeframe] ? data.timeframe : Object.keys(data.charts)[0];
            this.show(first);
        }

        // byUser: a timeframe button or the form's select was used; the other one then follows. The first
        // drawing leaves the form alone, so a trade to qualify never gets a timeframe the trader didn't pick.
        show(tf, byUser = false) {
            const rows = this.data?.charts[tf];
            if (!rows) return;
            this.timeframe = tf;
            const bars = rows.map(([time, open, high, low, close, cut]) => cut
                // The fill candle is cut at the fill: drawn lighter, so it reads as unfinished.
                ? { time, open, high, low, close, color: close >= open ? '#26a69a88' : '#ef535088', wickColor: '#888' }
                : { time, open, high, low, close });
            this.series.setData(bars);
            this.lastBar = bars[bars.length - 1];

            const buy = this.data.direction === 'Buy';
            this.markers.setMarkers(this.lastBar ? [{
                time: this.lastBar.time,
                position: buy ? 'atPriceBottom' : 'atPriceTop',
                price: this.data.entry,
                shape: buy ? 'arrowUp' : 'arrowDown',
                color: colors.entry,
                // The price itself is on the entry line's label, at the right.
                text: this.data.direction,
            }] : []);
            this.chart.timeScale().fitContent();
            this.showLegend(this.lastBar);

            document.querySelectorAll('[data-chart-tf]').forEach(b => b.classList.toggle('active', b.dataset.chartTf === tf));
            if (byUser) document.querySelectorAll('select[data-chart-tf-select]').forEach(s => { if ([...s.options].some(o => o.value === tf)) s.value = tf; });
        }

        showLegend(bar) {
            if (!bar || !this.legend) return;
            const d = this.data.digits;
            const t = new Date(bar.time * 1000).toISOString().slice(5, 16).replace('T', ' ');
            // Narrow (portrait) chart: the candle on one line, its prices on the next.
            this.legend.innerHTML = '';
            this.legend.append(`${this.timeframe} · ${t} UTC`, document.createElement('br'),
                `O ${bar.open.toFixed(d)} H ${bar.high.toFixed(d)} L ${bar.low.toFixed(d)} C ${bar.close.toFixed(d)}`);
        }
    }

    customElements.define('trade-chart', TradeChart);

    const chartOnPage = () => document.querySelector('trade-chart');
    document.addEventListener('click', e => {
        const button = e.target.closest?.('[data-chart-tf]');
        if (button) { e.preventDefault(); chartOnPage()?.show(button.dataset.chartTf, true); }
    });
    document.addEventListener('change', e => {
        if (e.target.matches?.('select[data-chart-tf-select]') && e.target.value) chartOnPage()?.show(e.target.value, true);
    });
})();
