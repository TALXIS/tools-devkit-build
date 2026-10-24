const root = document.getElementById("app");
const lines: string[] = [];
for (let index = 0; index < 3; index++) {
    lines.push(`fxdefault line ${index}`);
}
if (root) {
    root.textContent = lines.join(" | ");
}
