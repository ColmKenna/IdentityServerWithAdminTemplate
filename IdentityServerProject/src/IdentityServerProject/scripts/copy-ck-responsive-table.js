// Copies the built ck-responsive-table-webcomponent bundle from node_modules
// into wwwroot so it can be served as a static asset and referenced from
// Pages/Admin/Clients/Index.cshtml via <script type="module">.
const fs = require("fs");
const path = require("path");

const pkgDir = path.join(__dirname, "..", "node_modules", "@colmkenna", "ck-responsive-table-webcomponent");
const destDir = path.join(__dirname, "..", "wwwroot", "lib", "ck-responsive-table-webcomponent");

if (!fs.existsSync(pkgDir)) {
    console.warn(`[copy-ck-responsive-table] Package not found at ${pkgDir}. Run "npm install" first. Skipping copy.`);
    process.exit(0);
}

const distDir = path.join(pkgDir, "dist");
if (!fs.existsSync(distDir)) {
    console.warn(`[copy-ck-responsive-table] No dist/ folder found in package. Skipping copy.`);
    process.exit(0);
}

fs.rmSync(destDir, {recursive: true, force: true});
fs.mkdirSync(destDir, {recursive: true});
fs.cpSync(distDir, destDir, {recursive: true});

console.log(`[copy-ck-responsive-table] Copied dist assets to ${destDir}`);
