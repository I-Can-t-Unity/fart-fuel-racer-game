// Google Apps Script backend for the Fuel Pickup Collection Rate metric.
//
// The game POSTs events here (doPost) and appends them to the "events" sheet.
// The analytics page on GitHub Pages (docs/analytics) reads them back (doGet).
// Setup steps are in Analytics/README.md.

const SHEET_NAME = 'events';
const COLUMNS = [
  'session_id', 'timestamp_utc', 'platform', 'event', 'race_time_s', 'lane_spacing', 'row',
  'fuel_type', 'color', 'lane', 'suitable', 'collected_in_row', 'cubes_available', 'bag_full',
  'race_result', 'won', 'event_id', 'schema_version', 'build_id', 'scene', 'car_fuel', 'mix', 'fuel_amount',
];

// The game sends a JSON array of events as text/plain (no CORS preflight needed).
function doPost(e) {
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);
  try {
    const body = JSON.parse(e.postData.contents);
    const events = Array.isArray(body) ? body : [body];
    const sheet = getSheet_();
    const idColumn = COLUMNS.indexOf('event_id') + 1;
    const ids = new Set(sheet.getLastRow() > 1
      ? sheet.getRange(2, idColumn, sheet.getLastRow() - 1, 1).getValues().flat().filter(String) : []);
    const rows = [];
    for (const event of events) {
      if (!event.session_id || !event.event) throw new Error('Missing event identity');
      if (event.event_id && ids.has(String(event.event_id))) continue;
      if (event.event_id) ids.add(String(event.event_id));
      rows.push(COLUMNS.map(column => String(event[column] ?? '')));
    }
    if (rows.length > 0) {
      sheet.getRange(sheet.getLastRow() + 1, 1, rows.length, COLUMNS.length).setValues(rows);
    }
    return json_({ ok: true, added: rows.length });
  } catch (error) {
    return json_({ ok: false, error: String(error) });
  } finally {
    lock.releaseLock();
  }
}

// Returns every event as a JSON array of objects keyed by column name.
function doGet() {
  const values = getSheet_().getDataRange().getValues();
  const header = values.shift();
  const events = values.map(row => Object.fromEntries(header.map((column, i) => [column, row[i]])));
  return json_(events);
}

function getSheet_() {
  const spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
  let sheet = spreadsheet.getSheetByName(SHEET_NAME);
  if (!sheet) {
    sheet = spreadsheet.insertSheet(SHEET_NAME);
    // Plain text everywhere, so Sheets doesn't turn ids or times into dates and numbers.
    sheet.getRange(1, 1, sheet.getMaxRows(), COLUMNS.length).setNumberFormat('@');
    sheet.appendRow(COLUMNS);
    sheet.setFrozenRows(1);
  }
  // Append schema v2 columns without moving or deleting legacy data.
  if (sheet.getMaxColumns() < COLUMNS.length)
    sheet.insertColumnsAfter(sheet.getMaxColumns(), COLUMNS.length - sheet.getMaxColumns());
  sheet.getRange(1, 1, 1, COLUMNS.length).setValues([COLUMNS]);
  sheet.getRange(1, 1, sheet.getMaxRows(), COLUMNS.length).setNumberFormat('@');
  return sheet;
}

function json_(data) {
  return ContentService.createTextOutput(JSON.stringify(data)).setMimeType(ContentService.MimeType.JSON);
}
