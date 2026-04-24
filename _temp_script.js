const fs = require('fs');
const path = require('path');

const base = String.raw`C:\Users\edgvi\source\repos\Idealista`;

// 1. Copy logo
fs.copyFileSync(path.join(base, 'Images', 'logo.png'), path.join(base, 'wwwroot', 'logo.png'));
console.log('Logo copied');

// 3. Rename project files
const renames = [
  ['Idealista.csproj', 'InmoAlerts.csproj'],
  ['Idealista.csproj.user', 'InmoAlerts.csproj.user'],
  ['Idealista.sln', 'InmoAlerts.sln'],
];
for (const [from, to] of renames) {
  const src = path.join(base, from);
  const dst = path.join(base, to);
  if (fs.existsSync(src)) {
    fs.renameSync(src, dst);
    console.log('Renamed:', from, '->', to);
  } else if (fs.existsSync(dst)) {
    console.log('Already renamed:', to);
  } else {
    console.log('NOT FOUND:', from);
  }
}

// 5. Verify
console.log('');
console.log('--- Verification ---');
const checks = [
  path.join(base, 'wwwroot', 'logo.png'),
  path.join(base, 'InmoAlerts.csproj'),
  path.join(base, 'InmoAlerts.csproj.user'),
  path.join(base, 'InmoAlerts.sln'),
];
for (const p of checks) {
  console.log(fs.existsSync(p) ? 'EXISTS' : 'MISSING', p);
}
