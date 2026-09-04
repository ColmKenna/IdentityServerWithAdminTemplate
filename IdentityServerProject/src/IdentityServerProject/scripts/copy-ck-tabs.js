// Copies the built ck-tabs bundle from node_modules into wwwroot so it can be
// served as a static asset and referenced from Pages/Shared/_AdminLayout.cshtml
// via <script type="module">.
const fs = require("fs");
const path = require("path");

const pkgDir = path.join(__dirname, "..", "node_modules", "@colmkenna", "ck-tabs");
const destDir = path.join(__dirname, "..", "wwwroot", "lib", "ck-tabs-webcomponent");

if (!fs.existsSync(pkgDir)) {
    console.warn(`[copy-ck-tabs] Package not found at ${pkgDir}. Run "npm install" first. Skipping copy.`);
    process.exit(0);
}

const distDir = path.join(pkgDir, "dist");
if (!fs.existsSync(distDir)) {
    console.warn(`[copy-ck-tabs] No dist/ folder found in package. Skipping copy.`);
    process.exit(0);
}

fs.rmSync(destDir, {recursive: true, force: true});
fs.mkdirSync(destDir, {recursive: true});

const esmFile = path.join(distDir, "index.esm.js");
if (fs.existsSync(esmFile)) {
    fs.copyFileSync(esmFile, path.join(destDir, "index.esm.js"));
    console.log(`[copy-ck-tabs] Copied index.esm.js to ${destDir}`);
} else {
    fs.cpSync(distDir, destDir, {recursive: true});
    console.log(`[copy-ck-tabs] Copied dist assets to ${destDir}`);
}
