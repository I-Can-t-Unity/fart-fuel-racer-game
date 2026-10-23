// Run from project root: node Analytics/tests/dashboard-backend.test.cjs
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const html = fs.readFileSync('docs/analytics/index.html', 'utf8');
const script = html.split('<script>')[1].split('</script>')[0].replace(/loadLive\(\);\s*$/, '');
const elements = new Map();
function element(id) {
  if (!elements.has(id)) elements.set(id, { value: id === 'build-filter' ? 'alpha-analytics-v2' : id === 'scene-filter' ? 'AlphaRaceScene' : '', checked: false, addEventListener() {} });
  return elements.get(id);
}
const dashboard = vm.createContext({
  document: { getElementById: element, querySelectorAll: () => [], documentElement: {} },
  getComputedStyle: () => ({ getPropertyValue: () => '#888888' }), matchMedia: () => ({ addEventListener() {} }),
});
vm.runInContext(script, dashboard);
let serial = 0;
const event = (name, fields = {}) => ({ event: name, session_id: 'race-a', event_id: `e-${++serial}`, build_id: 'alpha-analytics-v2', scene: 'AlphaRaceScene', platform: 'WebGLPlayer', lane_spacing: '3.5', ...fields });
const row = (index, collected, extra = {}) => event('row_passed', { row: String(index), bag_full: '0', cubes_available: '3', collected_in_row: collected ? '1' : '0', fuel_type: collected ? 'Gasoline' : '', ...extra });
const rows = [row(0, true), row(1, false), row(2, true), row(3, false)];
const data = [event('race_start'), ...rows, event('pickup_collected', { fuel_type: 'Gasoline', color: 'straw_yellow' }), event('pickup_collected', { fuel_type: 'Gasoline', color: 'straw_yellow' }), row(4, false, { bag_full: '1' }), row(5, false, { cubes_available: '0' }), event('race_end', { won: '1' }), event('mix_created'), event('mix_used'), event('fuel_dumped')];
function summarise(events) { dashboard.fixture = events; return vm.runInContext('summarise(fixture)', dashboard); }
let summary = summarise(data);
assert.equal(summary.opportunities, 4);
assert.equal(summary.collectedRows, 2);
assert.equal(summary.byColor.find(c => c.fuel === 'Gasoline').collected, 2);
assert.equal(summary.ended, 1);
assert.equal(summary.mixesUsed, 1);
summary = summarise([...data, rows[0], { ...rows[0], event_id: 'different-id' }]);
assert.equal(summary.opportunities, 4, 'Deduplicate event IDs and session/row pairs');
assert.equal(summarise([]).opportunities, 0);
const otherRace = [event('race_start', { session_id: 'race-b', lane_spacing: '4.5' }), row(0, true, { session_id: 'race-b', lane_spacing: '4.5' })];
assert.equal(summarise([...data, ...otherRace]).bySpacing.length, 2);
dashboard.fixture = [...data, ...otherRace,
  event('race_start', { platform: 'OSXEditor' }), row(6, true, { platform: 'OSXEditor' }),
  row(7, true, { scene: 'LoopRaceScene' }), row(8, true, { build_id: 'old-build' })];
vm.runInContext('allEvents = fixture; source = "Test fixture"; render();', dashboard);
assert.equal(element('tile-opportunities').textContent, 5);
assert.equal(element('tile-rate').textContent, '60%');
assert.match(element('hypothesis').innerHTML, /Exploratory Alpha/);
assert.doesNotMatch(element('hypothesis').innerHTML, /Supported at|z-test/);
assert.doesNotMatch(element('chart-colors').innerHTML, /NaN|undefined/);
element('include-editor').checked = true;
vm.runInContext('render()', dashboard);
assert.equal(element('tile-opportunities').textContent, 6);
console.log('PASS: dashboard math, exclusions, deduplication, fuel identity, mechanics, cohorts, rendering.');

// Minimal Apps Script sheet emulation, seeded with a legacy 16-column sheet.
let cells = [['session_id','timestamp_utc','platform','event','race_time_s','lane_spacing','row','fuel_type','color','lane','suitable','collected_in_row','cubes_available','bag_full','race_result','won'], ['legacy-race','','WebGLPlayer','race_start']];
let columns = 16;
const sheet = {
  getLastRow: () => cells.length, getMaxRows: () => 1000, getMaxColumns: () => columns,
  insertColumnsAfter: (_index, count) => { columns += count; },
  setFrozenRows() {}, appendRow: row => cells.push(row),
  getRange(row, col, height, width) { return {
    setNumberFormat() {},
    getValues: () => Array.from({length: height}, (_, i) => Array.from({length: width}, (_, j) => cells[row+i-1]?.[col+j-1] ?? '')),
    setValues(values) { values.forEach((record, i) => { cells[row+i-1] ??= []; record.forEach((value, j) => { cells[row+i-1][col+j-1] = value; }); }); },
  }; },
  getDataRange() { return { getValues: () => cells.map(row => Array.from({length: columns}, (_, i) => row[i] ?? '')) }; },
};
const backend = vm.createContext({
  SpreadsheetApp: { getActiveSpreadsheet: () => ({ getSheetByName: () => sheet }) },
  LockService: { getScriptLock: () => ({ waitLock() {}, releaseLock() {} }) },
  ContentService: { MimeType: { JSON: 'json' }, createTextOutput: value => ({ value, setMimeType() { return this; } }) },
});
vm.runInContext(fs.readFileSync('Analytics/google-apps-script/Code.gs', 'utf8'), backend);
const post = events => { backend.request = { postData: { contents: JSON.stringify(events) } }; return JSON.parse(vm.runInContext('doPost(request).value', backend)); };
assert.equal(post(data).added, data.length);
assert.equal(cells[0].length, 23);
assert.equal(cells[1][0], 'legacy-race', 'Preserve legacy rows during schema migration');
assert.equal(post(data).added, 0, 'An acknowledged batch can be retried safely');
const fresh = event('race_start');
assert.equal(post([fresh, fresh]).added, 1, 'Deduplicate within the same batch');
const before = cells.length;
assert.equal(post([{ event_id: 'bad' }]).ok, false);
assert.equal(cells.length, before);
assert.equal(post([{ event: 'race_start', session_id: 'old-client' }]).ok, true, 'Legacy deployed clients remain accepted');
const received = JSON.parse(vm.runInContext('doGet().value', backend));
assert.equal(received.find(e => e.event_id === fresh.event_id).build_id, 'alpha-analytics-v2');
console.log('PASS: backend schema migration, legacy compatibility, acknowledgement, deduplication, malformed requests, readback.');
