// Copies the built ck-responsive-table-webcomponent bundle from node_modules
// into wwwroot so it can be served as a static asset and referenced from
// Pages/Shared/_AdminLayout.cshtml via <script type="module">.
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

const esmFile = path.join(distDir, "index.esm.js");
if (fs.existsSync(esmFile)) {
    fs.copyFileSync(esmFile, path.join(destDir, "index.esm.js"));
    console.log(`[copy-ck-responsive-table] Copied index.esm.js to ${destDir}`);
} else {
    fs.cpSync(distDir, destDir, {recursive: true});
    console.log(`[copy-ck-responsive-table] Copied dist assets to ${destDir}`);
}
