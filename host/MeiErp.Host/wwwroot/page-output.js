(function () {
    function safeName(value) {
        return (value || "MEI ERP")
            .replace(/[\\/:*?"<>|]+/g, " ")
            .replace(/\s+/g, " ")
            .trim()
            .slice(0, 80) || "MEI ERP";
    }

    function visible(element) {
        const style = window.getComputedStyle(element);
        return style.display !== "none" && style.visibility !== "hidden";
    }

    function cellText(cell) {
        const clone = cell.cloneNode(true);
        clone.querySelectorAll("button, a, svg, input, textarea, select, .mud-icon-root").forEach(x => x.remove());
        return (clone.innerText || clone.textContent || "").replace(/\s+/g, " ").trim();
    }

    function xml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&apos;");
    }

    function worksheet(table, index) {
        const rows = Array.from(table.querySelectorAll("tr")).filter(visible);
        if (!rows.length) return "";
        const caption = table.closest(".mud-paper")?.querySelector("h1,h2,h3,h4,h5,h6,.mud-typography-subtitle1")?.textContent;
        const name = safeName(caption || `Table ${index + 1}`).slice(0, 31);
        const body = rows.map(row => {
            const cells = Array.from(row.querySelectorAll(":scope > th, :scope > td"))
                .filter(visible)
                .map(cell => `<Cell><Data ss:Type="String">${xml(cellText(cell))}</Data></Cell>`)
                .join("");
            return cells ? `<Row>${cells}</Row>` : "";
        }).join("");
        return body ? `<Worksheet ss:Name="${xml(name)}"><Table>${body}</Table></Worksheet>` : "";
    }

    function tableData(table, index) {
        const rows = Array.from(table.querySelectorAll("tr")).filter(visible);
        if (!rows.length) return null;
        let headers = Array.from(rows[0].querySelectorAll(":scope > th")).filter(visible).map(cellText);
        let dataRows = rows.slice(headers.length ? 1 : 0);
        if (!headers.length) {
            const count = Array.from(rows[0].querySelectorAll(":scope > td")).filter(visible).length;
            headers = Array.from({ length: count }, (_, i) => `Column ${i + 1}`);
        }
        const data = dataRows.map(row => Array.from(row.querySelectorAll(":scope > td"))
            .filter(visible).map(cellText)).filter(row => row.some(Boolean));
        const caption = table.closest(".mud-paper")?.querySelector("h1,h2,h3,h4,h5,h6,.mud-typography-subtitle1")?.textContent;
        return headers.length ? { caption: safeName(caption || `Table ${index + 1}`), headers, rows: data } : null;
    }

    function formFields(root) {
        return Array.from(root.querySelectorAll("input, textarea, select"))
            .filter(element => visible(element) && element.type !== "hidden" && element.type !== "password")
            .map(element => {
                const box = element.closest(".mud-input-control") || element.parentElement;
                const label = box?.querySelector("label")?.textContent || element.getAttribute("aria-label") || element.name;
                let value = element.value;
                if (element.type === "checkbox") value = element.checked ? "Yes" : "No";
                return { label: (label || "Field").replace(/\s+/g, " ").trim(), value: value || "" };
            })
            .filter(field => field.label && field.value);
    }

    function pageText(root) {
        const clone = root.cloneNode(true);
        clone.querySelectorAll("table, input, textarea, select, button, a, svg, script, style, .mud-icon-root").forEach(x => x.remove());
        return (clone.innerText || clone.textContent || "").replace(/\s+/g, " ").trim().slice(0, 8000);
    }

    async function downloadPdf() {
        const root = document.querySelector(".app-page") || document.body;
        const tables = Array.from(root.querySelectorAll("table")).filter(visible).map(tableData).filter(Boolean);
        const fields = formFields(root);
        const content = pageText(root);
        const heading = root.querySelector("h1,h2,h3,h4,h5,.mud-typography-h5")?.textContent;
        if (!tables.length && !fields.length && !content) return false;
        const response = await fetch("/page-output/pdf", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                title: safeName(heading || document.title),
                path: location.pathname,
                content,
                fields,
                tables
            })
        });
        if (!response.ok) throw new Error(await response.text() || "PDF generation failed");
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = `${safeName(heading || document.title)} ${new Date().toISOString().slice(0, 10)}.pdf`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        return true;
    }

    window.meiPageOutput = {
        pdf: downloadPdf,
        print: function () {
            window.print();
        },
        excel: function () {
            const root = document.querySelector(".app-page") || document.body;
            const sheets = Array.from(root.querySelectorAll("table"))
                .filter(visible)
                .map(worksheet)
                .filter(Boolean);
            if (!sheets.length) return false;

            const workbook = `<?xml version="1.0"?><?mso-application progid="Excel.Sheet"?>` +
                `<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" ` +
                `xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">${sheets.join("")}</Workbook>`;
            const blob = new Blob([workbook], { type: "application/vnd.ms-excel;charset=utf-8" });
            const url = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = url;
            link.download = `${safeName(document.title)} ${new Date().toISOString().slice(0, 10)}.xls`;
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            return true;
        }
    };
})();
