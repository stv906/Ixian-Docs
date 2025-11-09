import fs from "fs";
import path from "path";

import grayMatter from "gray-matter"

type DocNode = {
    title: string;
    href: string;
    items?: DocNode[];
};

const ROOT = path.resolve("contents/docs");
const OUT = path.resolve("settings/documents.ts");

function processMdxFile(filePath: string) {
    const rawMdx = fs.readFileSync(filePath, "utf-8")

    return grayMatter(rawMdx).data;
}

function scan(dir: string, base = ""): DocNode[] {
    const result: DocNode[] = [];
    const files = fs.readdirSync(dir, { withFileTypes: true });
    for (const f of files) {
        if (f.isDirectory()) {
            const sub = scan(path.join(dir, f.name), `${base}/${f.name}`);
            let res = null;
            if (fs.existsSync(`contents/docs/${base}/${f.name}/index.mdx`)) {
                res = processMdxFile(`contents/docs/${base}/${f.name}/index.mdx`);
            }
            if (sub.length > 0) {
                result.push({ title: res ? res.title : f.name, href: `${base}/${f.name}`, items: sub });
            } else {
                result.push({ title: res ? res.title : f.name, href: `${base}/${f.name}` });
            }

        } else if (f.isFile() && /\.(md|mdx)$/.test(f.name)) {
            const name = f.name.replace(/\.(md|mdx)$/, "");
            if (name === "index") continue;

            const res = processMdxFile(`contents/docs/${base}/${f.name}`);
            result.push({ title: res ? res.title : name.replace(/-/g, " "), href: `${base}/${name}` });
        }
    }
    return result;
}

const Documents = scan(ROOT);

const content = `// AUTO-GENERATED. DO NOT EDIT
import type { Path } from "@/lib/pageroutes";

export const Documents: Path[] = ${JSON.stringify(Documents, null, 2)};
`;

fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, content);
console.log(`Generated ${OUT}`);
