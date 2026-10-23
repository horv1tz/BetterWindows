#!/usr/bin/env node
// Generates C# and TypeScript protocol types from protocol/*.json.
//   node tools/gen-protocol.mjs          -> (re)write generated files
//   node tools/gen-protocol.mjs --check  -> exit 1 if generated files are stale
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const protoDir = path.join(root, 'protocol');
const outCs = path.join(root, 'src/BetterWindows.Core/Protocol/Generated');
const outTs = path.join(root, 'ui/src/lib/protocol/generated.ts');
const check = process.argv.includes('--check');

const PROTOCOL_VERSION = 1;
const PRIMS = new Set(['string', 'bool', 'int', 'long', 'double', 'any']);
const pascal = (s) => s.split(/[._-]/).map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join('');
const camel = (s) => s.charAt(0).toLowerCase() + s.slice(1);
const fail = (m) => { console.error('gen-protocol: ' + m); process.exit(2); };

// ---------- load ----------
const files = fs.readdirSync(protoDir).filter((f) => f.endsWith('.json')).sort();
const domains = files.map((f) => ({ file: f, ...JSON.parse(fs.readFileSync(path.join(protoDir, f), 'utf8')) }));
const enums = new Map();   // name -> {values, doc, domain}
const types = new Map();   // name -> {fields, doc, domain}
const messages = [];       // {type, kind:'event'|'call', dir, payload, params, result, doc, domain}

for (const d of domains) {
  if (!d.domain) fail(`${d.file}: missing "domain"`);
  for (const [n, v] of Object.entries(d.enums ?? {})) {
    if (enums.has(n) || types.has(n)) fail(`duplicate type name ${n} (${d.file})`);
    const values = Array.isArray(v) ? v : v.values;
    for (const w of values) {
      if (!/^[a-z][A-Za-z0-9]*$/.test(w)) fail(`${n}.${w}: enum wire values must be lowerCamelCase`);
    }
    enums.set(n, { values, doc: v.doc, domain: d.domain });
  }
  for (const [n, v] of Object.entries(d.types ?? {})) {
    if (enums.has(n) || types.has(n)) fail(`duplicate type name ${n} (${d.file})`);
    const { $doc, ...fields } = v;
    types.set(n, { fields, doc: $doc, domain: d.domain });
  }
  for (const [t, v] of Object.entries(d.events ?? {})) {
    messages.push({ type: t, kind: 'event', dir: v.dir, payload: v.payload ?? null, doc: v.doc, domain: d.domain });
  }
  for (const [t, v] of Object.entries(d.calls ?? {})) {
    messages.push({ type: t, kind: 'call', dir: 'ui', params: v.params ?? null, result: v.result ?? null, doc: v.doc, domain: d.domain });
  }
}

// ---------- parse + validate ----------
function parseType(expr) {
  let t = expr.trim(), nullable = false;
  if (t.endsWith('?')) { nullable = true; t = t.slice(0, -1); }
  let m;
  if (t.endsWith('[]')) return { kind: 'list', item: parseType(t.slice(0, -2)), nullable };
  if ((m = /^map<(.+)>$/.exec(t))) return { kind: 'map', item: parseType(m[1]), nullable };
  if (PRIMS.has(t)) return { kind: 'prim', name: t, nullable: nullable || t === 'any' };
  if (enums.has(t)) return { kind: 'enum', name: t, nullable };
  if (types.has(t)) return { kind: 'type', name: t, nullable };
  fail(`unknown type "${t}" in "${expr}"`);
}
const fieldDefs = (tname) => Object.entries(types.get(tname).fields).map(([name, def]) => {
  const spec = typeof def === 'string' ? { type: def } : def;
  return { name, spec, type: parseType(spec.type), doc: spec.doc };
});
for (const [n, t] of types) for (const f of fieldDefs(n)) {
  if (!/^[a-z][A-Za-z0-9]*$/.test(f.name)) fail(`${n}.${f.name}: property names must be lowerCamelCase`);
}
const seen = new Set();
for (const m of messages) {
  if (seen.has(m.type)) fail(`duplicate message type ${m.type}`);
  seen.add(m.type);
  if (m.kind === 'event' && !['host', 'ui'].includes(m.dir)) fail(`${m.type}: event dir must be "host" or "ui"`);
  for (const k of ['payload', 'params', 'result']) if (m[k]) parseType(m[k]);
}

// ---------- C# ----------
const csType = (t) => {
  let s;
  switch (t.kind) {
    case 'prim': s = { string: 'string', bool: 'bool', int: 'int', long: 'long', double: 'double', any: 'System.Text.Json.Nodes.JsonNode' }[t.name]; break;
    case 'enum': case 'type': s = t.name; break;
    case 'list': s = `IReadOnlyList<${csType({ ...t.item, nullable: false })}${t.item.nullable ? '?' : ''}>`; break;
    case 'map': s = `IReadOnlyDictionary<string, ${csType({ ...t.item, nullable: false })}${t.item.nullable ? '?' : ''}>`; break;
  }
  return t.nullable ? s + '?' : s;
};
const csLiteral = (v, t) => {
  if (t.kind === 'enum') return `${t.name}.${pascal(v)}`;
  if (typeof v === 'string') return JSON.stringify(v);
  if (typeof v === 'boolean') return v ? 'true' : 'false';
  if (typeof v === 'number') return t.kind === 'prim' && t.name === 'double' && Number.isInteger(v) ? `${v}.0` : String(v);
  fail(`bad default ${JSON.stringify(v)}`);
};
const csDefault = (f) => {
  const { type: t, spec } = f;
  if (spec.default !== undefined) {
    if (Array.isArray(spec.default)) {
      if (spec.default.length === 0) return t.kind === 'list' ? `Array.Empty<${csType({ ...t.item, nullable: false })}>()` : fail('[] default on non-list');
      return `new ${csType({ ...t.item, nullable: false })}[] { ${spec.default.map((x) => csLiteral(x, t.item)).join(', ')} }`;
    }
    return csLiteral(spec.default, t);
  }
  if (t.nullable) return null;
  switch (t.kind) {
    case 'prim': return { string: '""', bool: 'false', int: '0', long: '0', double: '0.0' }[t.name];
    case 'enum': return `${t.name}.${pascal(enums.get(t.name).values[0])}`;
    case 'type': return 'new()';
    case 'list': return `Array.Empty<${csType({ ...t.item, nullable: false })}${t.item.nullable ? '?' : ''}>()`;
    case 'map': return `new Dictionary<string, ${csType({ ...t.item, nullable: false })}${t.item.nullable ? '?' : ''}>()`;
  }
};
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const docCsx = (doc, indent) => (doc ? `${indent}/// <summary>\n${esc(doc).split('\n').map((l) => `${indent}/// ${l}`).join('\n')}\n${indent}/// </summary>\n` : '');

const HEADER = '// <auto-generated>\n// Generated by tools/gen-protocol.mjs from protocol/*.json. DO NOT EDIT.\n// </auto-generated>\n';
let cs = HEADER + '#nullable enable\nusing System.Text.Json.Serialization;\n\nnamespace BetterWindows.Core.Protocol;\n\n';
cs += `public static class ProtocolInfo\n{\n    public const int Version = ${PROTOCOL_VERSION};\n}\n\n`;
for (const [n, e] of [...enums].sort((a, b) => a[0].localeCompare(b[0]))) {
  cs += docCsx(e.doc, '') + '[JsonConverter(typeof(CamelEnumConverter))]\n';
  cs += `public enum ${n}\n{\n${e.values.map((v) => `    ${pascal(v)},`).join('\n')}\n}\n\n`;
}
for (const [n, t] of [...types].sort((a, b) => a[0].localeCompare(b[0]))) {
  cs += docCsx(t.doc, '') + `public sealed record ${n}\n{\n`;
  for (const f of fieldDefs(n)) {
    const def = csDefault(f);
    cs += docCsx(f.doc, '    ');
    cs += `    [JsonPropertyName("${f.name}")] public ${csType(f.type)} ${pascal(f.name)} { get; init; }${def ? ` = ${def};` : ''}\n`;
  }
  cs += '}\n\n';
}
// message constants + catalog
let msgs = HEADER + '#nullable enable\nnamespace BetterWindows.Core.Protocol;\n\n';
msgs += 'public enum MessageKind { HostEvent, UiEvent, Call }\n\n';
msgs += 'public sealed record MessageDef(string Type, MessageKind Kind, string Domain, Type? Payload, Type? Result)\n{\n    /// <summary>For calls: the params type; for events: the payload type.</summary>\n    public Type? Params => Payload;\n}\n\n';
msgs += '/// <summary>Wire names of every protocol message.</summary>\npublic static class Msg\n{\n';
for (const m of messages) {
  msgs += docCsx(`${m.kind === 'call' ? 'Call (ui -> host)' : m.dir === 'host' ? 'Event (host -> ui)' : 'Event (ui -> host)'}` + (m.doc ? `. ${m.doc}` : ''), '    ');
  msgs += `    public const string ${pascal(m.type)} = "${m.type}";\n`;
}
msgs += '}\n\n/// <summary>Reflection-free catalog of all messages; used by tests to verify every call has a handler.</summary>\npublic static class MessageCatalog\n{\n    public static readonly IReadOnlyList<MessageDef> All = new MessageDef[]\n    {\n';
for (const m of messages) {
  const k = m.kind === 'call' ? 'MessageKind.Call' : m.dir === 'host' ? 'MessageKind.HostEvent' : 'MessageKind.UiEvent';
  const p = m.kind === 'call' ? m.params : m.payload;
  msgs += `        new("${m.type}", ${k}, "${m.domain}", ${p ? `typeof(${p})` : 'null'}, ${m.kind === 'call' && m.result ? `typeof(${m.result})` : 'null'}),\n`;
}
msgs += '    };\n}\n';

// ---------- TypeScript ----------
const tsType = (t) => {
  let s;
  switch (t.kind) {
    case 'prim': s = { string: 'string', bool: 'boolean', int: 'number', long: 'number', double: 'number', any: 'unknown' }[t.name]; break;
    case 'enum': case 'type': s = t.name; break;
    case 'list': s = `${tsType({ ...t.item, nullable: false }).replace(/^(.*\|.*)$/, '($1)')}${t.item.nullable ? '' : ''}[]`; break;
    case 'map': s = `Record<string, ${tsType({ ...t.item, nullable: false })}>`; break;
  }
  return s;
};
const docTs = (doc, indent) => (doc ? `${indent}/** ${doc.replace(/\*\//g, '* /').split('\n').join(`\n${indent} * `)} */\n` : '');
let ts = '// Generated by tools/gen-protocol.mjs from protocol/*.json. DO NOT EDIT.\n/* eslint-disable */\n\n';
ts += `export const PROTOCOL_VERSION = ${PROTOCOL_VERSION};\n\n`;
for (const [n, e] of [...enums].sort((a, b) => a[0].localeCompare(b[0]))) {
  ts += docTs(e.doc, '') + `export type ${n} = ${e.values.map((v) => `'${v}'`).join(' | ')};\n`;
  ts += `export const ${n}Values: readonly ${n}[] = [${e.values.map((v) => `'${v}'`).join(', ')}];\n\n`;
}
for (const [n, t] of [...types].sort((a, b) => a[0].localeCompare(b[0]))) {
  ts += docTs(t.doc, '') + `export interface ${n} {\n`;
  for (const f of fieldDefs(n)) {
    ts += docTs(f.doc, '  ') + `  ${f.name}${f.type.nullable ? '?' : ''}: ${tsType(f.type)};\n`;
  }
  ts += '}\n\n';
}
const tsDefault = (f) => {
  const { type: t, spec } = f;
  if (spec.default !== undefined) return JSON.stringify(spec.default);
  if (t.nullable) return null;
  switch (t.kind) {
    case 'prim': return { string: "''", bool: 'false', int: '0', long: '0', double: '0' }[t.name];
    case 'enum': return `'${enums.get(t.name).values[0]}'`;
    case 'type': return `default${t.name}()`;
    case 'list': return '[]';
    case 'map': return '{}';
  }
};
ts += '// ---- default factories (mirror the C# initialisers) ----\n';
for (const [n] of [...types].sort((a, b) => a[0].localeCompare(b[0]))) {
  const parts = fieldDefs(n).map((f) => [f.name, tsDefault(f)]).filter(([, d]) => d !== null);
  ts += `export function default${n}(): ${n} {\n  return {${parts.length ? '\n' + parts.map(([k, d]) => `    ${k}: ${d},`).join('\n') + '\n  ' : ''}};\n}\n\n`;
}
const tsOr = (x) => (x ? x : 'void');
ts += '/** Events pushed from the host to the UI: type -> payload. */\nexport interface HostEvents {\n';
for (const m of messages.filter((m) => m.kind === 'event' && m.dir === 'host')) ts += docTs(m.doc, '  ') + `  '${m.type}': ${tsOr(m.payload)};\n`;
ts += '}\n\n/** Events sent from the UI to the host (fire-and-forget): type -> payload. */\nexport interface UiEvents {\n';
for (const m of messages.filter((m) => m.kind === 'event' && m.dir === 'ui')) ts += docTs(m.doc, '  ') + `  '${m.type}': ${tsOr(m.payload)};\n`;
ts += '}\n\n/** Request/response calls (UI -> host): type -> { params, result }. */\nexport interface Calls {\n';
for (const m of messages.filter((m) => m.kind === 'call')) ts += docTs(m.doc, '  ') + `  '${m.type}': { params: ${tsOr(m.params)}; result: ${tsOr(m.result)} };\n`;
ts += '}\n\nexport type HostEventType = keyof HostEvents;\nexport type UiEventType = keyof UiEvents;\nexport type CallType = keyof Calls;\n\n';
ts += 'export const HOST_EVENT_TYPES: readonly HostEventType[] = [\n' + messages.filter((m) => m.kind === 'event' && m.dir === 'host').map((m) => `  '${m.type}',`).join('\n') + '\n];\n';
ts += 'export const CALL_TYPES: readonly CallType[] = [\n' + messages.filter((m) => m.kind === 'call').map((m) => `  '${m.type}',`).join('\n') + '\n];\n';

// ---------- Markdown reference ----------
const outMd = path.join(root, 'docs/PROTOCOL.md');
const typeStr = (expr) => '`' + expr + '`';
let md = '<!-- Generated by tools/gen-protocol.mjs from protocol/*.json. DO NOT EDIT. -->\n# Protocol reference\n\n';
md += `Protocol version **${PROTOCOL_VERSION}**. Semantics (envelopes, ordering, coordinates, error codes) are in [IPC.md](IPC.md).\n\n`;
for (const d of domains) {
  md += `## ${d.domain}\n\n`;
  const ms = messages.filter((m) => m.domain === d.domain);
  if (ms.length) {
    md += '| message | kind | payload / params | result | notes |\n|---|---|---|---|---|\n';
    for (const m of ms) {
      const kind = m.kind === 'call' ? 'call (ui→host)' : m.dir === 'host' ? 'event (host→ui)' : 'event (ui→host)';
      const p = m.kind === 'call' ? m.params : m.payload;
      md += `| \`${m.type}\` | ${kind} | ${p ? typeStr(p) : '–'} | ${m.kind === 'call' ? (m.result ? typeStr(m.result) : '–') : ''} | ${(m.doc ?? '').replace(/\|/g, '\\|').replace(/\n/g, ' ')} |\n`;
    }
    md += '\n';
  }
  for (const [n, e] of enums) if (e.domain === d.domain) md += `**enum \`${n}\`**: ${e.values.map((v) => '`' + v + '`').join(' · ')}${e.doc ? ' — ' + e.doc : ''}\n\n`;
  for (const [n, t] of types) if (t.domain === d.domain) {
    md += `**\`${n}\`**${t.doc ? ' — ' + t.doc.replace(/\n/g, ' ') : ''}\n\n| field | type | default | notes |\n|---|---|---|---|\n`;
    for (const f of fieldDefs(n)) md += `| \`${f.name}\` | ${typeStr(f.spec.type)} | ${f.spec.default !== undefined ? '`' + JSON.stringify(f.spec.default) + '`' : ''} | ${(f.doc ?? '').replace(/\|/g, '\\|').replace(/\n/g, ' ')} |\n`;
    md += '\n';
  }
}

// ---------- write / check ----------
const outputs = new Map([
  [path.join(outCs, 'Types.g.cs'), cs],
  [path.join(outCs, 'Messages.g.cs'), msgs],
  [outTs, ts],
  [outMd, md],
]);
let stale = false;
for (const [file, content] of outputs) {
  const existing = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : null;
  if (check) { if (existing !== content) { console.error(`stale: ${path.relative(root, file)}`); stale = true; } continue; }
  fs.mkdirSync(path.dirname(file), { recursive: true });
  if (existing !== content) fs.writeFileSync(file, content);
}
if (check) process.exit(stale ? 1 : 0);
console.log(`gen-protocol: ${enums.size} enums, ${types.size} types, ${messages.length} messages from ${files.length} domains`);
