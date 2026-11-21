// Prüft die JavaScript-Syntax der Oberfläche (src/Nuro/wwwroot/index.html), bevor die exe gebaut wird.
const fs = require('fs');
const file = 'src/Nuro/wwwroot/index.html';
const html = fs.readFileSync(file, 'utf8');

if (!/<\/html>\s*$/i.test(html)) {
  console.error(`::error file=${file}::Datei endet nicht mit </html> – vermutlich abgeschnitten.`);
  process.exit(1);
}
const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)];
if (scripts.length === 0) {
  console.error(`::error file=${file}::Kein <script>-Block gefunden.`);
  process.exit(1);
}
let failed = false;
scripts.forEach((m, i) => {
  try { new Function(m[1]); }
  catch (e) {
    const line = html.slice(0, m.index).split('\n').length;
    console.error(`::error file=${file},line=${line}::Script-Block ${i + 1} (ab Zeile ${line}): ${e.message}`);
    failed = true;
  }
});
if (failed) process.exit(1);
console.log(`OK: ${scripts.length} Script-Block(e) syntaktisch korrekt.`);
