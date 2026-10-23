// Google Apps Script backend for the Fuel Pickup Collection Rate metric.
//
// The game POSTs events here (doPost) and appends them to the "events" sheet.
// The analytics page on GitHub Pages (docs/analytics) reads them back (doGet).
// Setup steps are in Analytics/README.md.

const SHEET_NAME = 'events';
const COLUMNS = [
  'session_id', 'timestamp_utc', 'platform', 'event', 'race_time_s', 'lane_spacing', 'row',
  'fuel_type', 'color', 'lane', 'suitable', 'collected_in_row', 'cubes_available', 'bag_full',
  'race_result', 'won',
];

// The game sends a JSON array of events as text/plain (no CORS preflight needed).
function doPost(e) {
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);
  try {
    const body = JSON.parse(e.postData.contents);
    const events = Array.isArray(body) ? body : [body];
    const rows = events.map(event => COLUMNS.map(column => String(event[column] ?? '')));
    if (rows.length > 0) {
      const sheet = getSheet_();
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
  return sheet;
}

function json_(data) {
  return ContentService.createTextOutput(JSON.stringify(data)).setMimeType(ContentService.MimeType.JSON);
}
