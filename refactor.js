const fs = require('fs');
const path = require('path');
const base = 'C:\\Users\\edgvi\\source\\repos\\Idealista';

// Step 1: Copy logo
try {
  const src = path.join(base, 'Images', 'logo.png');
  const dst = path.join(base, 'wwwroot', 'logo.png');
  fs.mkdirSync(path.dirname(dst), { recursive: true });
  fs.copyFileSync(src, dst);
  console.log('[OK] Step 1: Copied Images\\logo.png -> wwwroot\\logo.png');
} catch(e) { console.log('[FAIL] Step 1:', e.message); }

// Step 2: Replace in all .cs files recursively
function findFiles(dir, ext, list) {
  list = list || [];
  var items = fs.readdirSync(dir);
  for (var i = 0; i < items.length; i++) {
    var full = path.join(dir, items[i]);
    var stat = fs.statSync(full);
    if (stat.isDirectory()) findFiles(full, ext, list);
    else if (items[i].endsWith(ext)) list.push(full);
  }
  return list;
}
var csFiles = findFiles(base, '.cs');
var csReplaced = 0;
for (var i = 0; i < csFiles.length; i++) {
  var f = csFiles[i];
  var content = fs.readFileSync(f, 'utf8');
  var orig = content;
  content = content.replace(/namespace Idealista\b/g, 'namespace InmoAlerts');
  content = content.replace(/using Idealista\./g, 'using InmoAlerts.');
  if (content !== orig) {
    fs.writeFileSync(f, content, 'utf8');
    csReplaced++;
    console.log('[OK] Step 2: Updated ' + f.replace(base + '\\', ''));
  }
}
console.log('[OK] Step 2: Replaced in ' + csReplaced + ' of ' + csFiles.length + ' .cs files');

// Step 3: Rename files
var renames = [
  ['Idealista.csproj', 'InmoAlerts.csproj'],
  ['Idealista.csproj.user', 'InmoAlerts.csproj.user'],
  ['Idealista.sln', 'InmoAlerts.sln']
];
for (var j = 0; j < renames.length; j++) {
  var from = renames[j][0], to = renames[j][1];
  var srcPath = path.join(base, from);
  var dstPath = path.join(base, to);
  try {
    fs.renameSync(srcPath, dstPath);
    console.log('[OK] Step 3: Renamed ' + from + ' -> ' + to);
  } catch(e) {
    if (e.code === 'ENOENT') console.log('[SKIP] Step 3: ' + from + ' not found (skipped)');
    else console.log('[FAIL] Step 3: ' + from + ' -> ' + e.message);
  }
}

// Step 4: Replace inside InmoAlerts.sln
try {
  var slnPath = path.join(base, 'InmoAlerts.sln');
  var sln = fs.readFileSync(slnPath, 'utf8');
  var slnOrig = sln;
  sln = sln.replace(/"Idealista", "Idealista\.csproj"/g, '"InmoAlerts", "InmoAlerts.csproj"');
  if (sln !== slnOrig) {
    fs.writeFileSync(slnPath, sln, 'utf8');
    console.log('[OK] Step 4: Updated InmoAlerts.sln project reference');
  } else {
    console.log('[INFO] Step 4: No matching text found in InmoAlerts.sln (may already be updated)');
  }
} catch(e) { console.log('[FAIL] Step 4:', e.message); }

// Step 5: Verify
console.log('\n--- Verification ---');
var checks = ['wwwroot\\logo.png', 'InmoAlerts.csproj', 'InmoAlerts.sln'];
for (var k = 0; k < checks.length; k++) {
  var p = path.join(base, checks[k]);
  console.log((fs.existsSync(p) ? '[EXISTS]' : '[MISSING]') + ' ' + checks[k]);
}
var oldFiles = ['Idealista.csproj', 'Idealista.sln'];
for (var m = 0; m < oldFiles.length; m++) {
  var op = path.join(base, oldFiles[m]);
  console.log((!fs.existsSync(op) ? '[REMOVED]' : '[STILL EXISTS]') + ' ' + oldFiles[m]);
}
