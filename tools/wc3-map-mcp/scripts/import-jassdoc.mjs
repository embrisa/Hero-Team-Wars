#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

export const CANONICAL_REPOSITORY = "https://github.com/lep/jassdoc";
export const PINNED_COMMIT = "deddec452ec16ea355ca0aa47046b88d416dbc65";
export const SOURCE_FILES = ["common.j", "Blizzard.j", "builtin-types.j"];

function usage() {
  return [
    "Usage: node import-jassdoc.mjs --source-root <jassdoc checkout> [--output <file>]",
    "       [--source-commit <40-hex commit>] [--source-repository <url>]",
  ].join("\n");
}

function parseArguments(argv) {
  const options = {
    output: path.resolve(import.meta.dirname, "../map-engine/data/jassdoc/jass-api.json"),
    sourceCommit: PINNED_COMMIT,
    sourceRepository: CANONICAL_REPOSITORY,
  };

  for (let index = 2; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--help" || argument === "-h") {
      console.log(usage());
      process.exit(0);
    }
    const next = argv[index + 1];
    if (!["--source-root", "--output", "--source-commit", "--source-repository"].includes(argument) || next === undefined) {
      throw new Error(`Unknown or incomplete argument '${argument}'.\n${usage()}`);
    }
    if (argument === "--source-root") options.sourceRoot = path.resolve(next);
    if (argument === "--output") options.output = path.resolve(next);
    if (argument === "--source-commit") options.sourceCommit = next;
    if (argument === "--source-repository") options.sourceRepository = next;
    index += 1;
  }

  if (!options.sourceRoot) throw new Error(`--source-root is required.\n${usage()}`);
  if (!/^[0-9a-f]{40}$/i.test(options.sourceCommit)) {
    throw new Error(`--source-commit must be a 40-character hexadecimal commit, got '${options.sourceCommit}'.`);
  }
  if (options.sourceRepository.length === 0) throw new Error("--source-repository cannot be empty.");
  return options;
}

function compareStrings(left, right) {
  if (left < right) return -1;
  if (left > right) return 1;
  return 0;
}

function trimBlankEdges(lines) {
  let start = 0;
  let end = lines.length;
  while (start < end && lines[start].trim() === "") start += 1;
  while (end > start && lines[end - 1].trim() === "") end -= 1;
  return lines.slice(start, end);
}

function parseDocumentation(rawLines) {
  const nonBlank = rawLines.filter((line) => line.trim() !== "");
  const decorated = nonBlank.length > 0 && nonBlank.every((line) => /^\s*\*(?:\s|$)/.test(line));
  const lines = rawLines.map((line) => {
    if (decorated) return line.replace(/^\s*\* ?/, "");
    return line;
  });

  const general = [];
  const tags = [];
  let currentTag = null;
  const finishTag = () => {
    if (!currentTag) return;
    const value = trimBlankEdges(currentTag.lines).join("\n").trim();
    tags.push({ name: currentTag.name, value });
    currentTag = null;
  };

  for (const line of lines) {
    const match = line.match(/^\s*@([A-Za-z][A-Za-z0-9_-]*)(?:\s+(.*))?\s*$/);
    if (match) {
      finishTag();
      currentTag = { name: match[1], lines: match[2] === undefined ? [] : [match[2].trimEnd()] };
      continue;
    }
    if (currentTag) currentTag.lines.push(line.trimEnd());
    else general.push(line.trimEnd());
  }
  finishTag();

  const parameters = new Map();
  const annotations = [];
  for (const tag of tags) {
    if (tag.name.toLowerCase() !== "param") {
      annotations.push({ name: tag.name, value: tag.value });
      continue;
    }
    const parameter = tag.value.match(/^(\S+)(?:\s+([\s\S]*))?$/);
    if (!parameter) {
      annotations.push({ name: tag.name, value: tag.value });
      continue;
    }
    parameters.set(parameter[1], (parameter[2] ?? "").trim());
  }

  return {
    documentation: trimBlankEdges(general).join("\n").trim(),
    annotations,
    parameterDocumentation: parameters,
  };
}

function parseParameters(clause, source, line) {
  const value = clause.trim();
  if (value === "nothing") return [];
  if (value.length === 0) throw new Error(`${source}:${line}: function parameter list is empty; use 'nothing'.`);

  return value.split(",").map((part) => {
    const match = part.trim().match(/^([A-Za-z][A-Za-z0-9_]*)(?:\s+array)?\s+([A-Za-z][A-Za-z0-9_]*)$/);
    if (!match) throw new Error(`${source}:${line}: cannot parse function parameter '${part.trim()}'.`);
    return { name: match[2], type: match[1] };
  });
}

function addDocumentation(symbol, documentation) {
  symbol.documentation = documentation.documentation;
  symbol.annotations = documentation.annotations;
  for (const parameter of symbol.parameters) {
    const description = documentation.parameterDocumentation.get(parameter.name);
    if (description) parameter.documentation = description;
  }
}

function createFunctionSymbol(match, kind, source, line, declaration, documentation) {
  const parameters = parseParameters(match[2], source, line);
  const symbol = {
    name: match[1],
    kind,
    source,
    declaration,
    parameters,
    return_type: match[3],
    documentation: "",
    annotations: [],
    source_line: line,
  };
  addDocumentation(symbol, documentation);
  return symbol;
}

function createTypeSymbol(match, source, line, declaration, documentation) {
  const symbol = {
    name: match[1],
    kind: "type",
    source,
    declaration,
    parameters: [],
    return_type: null,
    documentation: "",
    annotations: [],
    source_line: line,
    extends: match[2],
  };
  addDocumentation(symbol, documentation);
  return symbol;
}

function createGlobalSymbol(match, source, line, declaration, documentation) {
  const symbol = {
    name: match[3],
    kind: "global",
    source,
    declaration,
    parameters: [],
    return_type: match[2],
    documentation: "",
    annotations: [],
    source_line: line,
  };
  addDocumentation(symbol, documentation);
  return symbol;
}

function stripLineComment(value) {
  const comment = value.indexOf("//");
  return comment < 0 ? value : value.slice(0, comment);
}

function parseSource(filePath, sourceName) {
  const text = fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, "").replace(/\r\n?/g, "\n");
  const lines = text.split("\n");
  const symbols = [];
  let inDoc = false;
  let docLines = [];
  let pendingDocumentation = parseDocumentation([]);
  let inGlobals = false;

  for (let index = 0; index < lines.length; index += 1) {
    const lineNumber = index + 1;
    let remainder = lines[index];
    let code = "";
    let completedDocumentation = false;

    while (remainder.length > 0 || inDoc) {
      if (inDoc) {
        const end = remainder.indexOf("*/");
        if (end < 0) {
          docLines.push(remainder);
          remainder = "";
          break;
        }
        docLines.push(remainder.slice(0, end));
        remainder = remainder.slice(end + 2);
        inDoc = false;
        completedDocumentation = true;
        continue;
      }

      const start = remainder.indexOf("/**");
      const comment = remainder.indexOf("//");
      if (comment >= 0 && (start < 0 || comment < start)) {
        code += remainder.slice(0, comment);
        remainder = "";
        break;
      }
      if (start < 0) {
        code += remainder;
        remainder = "";
        break;
      }
      code += remainder.slice(0, start);
      remainder = remainder.slice(start + 3);
      inDoc = true;
      docLines = [];
    }

    if (completedDocumentation) {
      pendingDocumentation = parseDocumentation(docLines);
      docLines = [];
    }

    const declaration = stripLineComment(code).trim();
    if (declaration.length === 0) continue;

    if (declaration === "globals") {
      inGlobals = true;
      pendingDocumentation = parseDocumentation([]);
      continue;
    }
    if (declaration === "endglobals") {
      inGlobals = false;
      pendingDocumentation = parseDocumentation([]);
      continue;
    }

    const typeMatch = declaration.match(/^type\s+([A-Za-z][A-Za-z0-9_]*)\s+extends\s+([A-Za-z][A-Za-z0-9_]*)$/);
    if (typeMatch) {
      symbols.push(createTypeSymbol(typeMatch, sourceName, lineNumber, declaration, pendingDocumentation));
      pendingDocumentation = parseDocumentation([]);
      continue;
    }

    const nativeMatch = declaration.match(/^(?:constant\s+)?native\s+([A-Za-z][A-Za-z0-9_]*)\s+takes\s+([\s\S]+?)\s+returns\s+([A-Za-z][A-Za-z0-9_]*)$/);
    if (nativeMatch) {
      symbols.push(createFunctionSymbol(nativeMatch, "native", sourceName, lineNumber, declaration, pendingDocumentation));
      pendingDocumentation = parseDocumentation([]);
      continue;
    }

    const functionMatch = declaration.match(/^(?:constant\s+)?function\s+([A-Za-z][A-Za-z0-9_]*)\s+takes\s+([\s\S]+?)\s+returns\s+([A-Za-z][A-Za-z0-9_]*)$/);
    if (functionMatch) {
      symbols.push(createFunctionSymbol(functionMatch, "function", sourceName, lineNumber, declaration, pendingDocumentation));
      pendingDocumentation = parseDocumentation([]);
      continue;
    }

    if (inGlobals) {
      const globalMatch = declaration.match(/^(constant\s+)?([A-Za-z][A-Za-z0-9_]*)(?:\s+array)?\s+([A-Za-z][A-Za-z0-9_]*)(?:\s*=\s*[\s\S]*)?$/);
      if (globalMatch) {
        symbols.push(createGlobalSymbol(globalMatch, sourceName, lineNumber, declaration, pendingDocumentation));
        pendingDocumentation = parseDocumentation([]);
        continue;
      }
    }

    // A non-declaration code line separates a documentation block from the
    // next declaration. This also keeps examples in source comments inert.
    pendingDocumentation = parseDocumentation([]);
  }

  if (inDoc) throw new Error(`${sourceName}: unterminated /** documentation comment.`);
  return symbols;
}

export function importJassdoc({ sourceRoot, output, sourceCommit = PINNED_COMMIT, sourceRepository = CANONICAL_REPOSITORY }) {
  if (!fs.existsSync(sourceRoot)) throw new Error(`jassdoc source root does not exist: ${sourceRoot}`);
  const symbols = [];
  for (const sourceName of SOURCE_FILES) {
    const sourcePath = path.join(sourceRoot, sourceName);
    if (!fs.existsSync(sourcePath)) throw new Error(`Required jassdoc source file is missing: ${sourcePath}`);
    symbols.push(...parseSource(sourcePath, sourceName));
  }

  symbols.sort((left, right) =>
    compareStrings(left.name, right.name) ||
    compareStrings(left.kind, right.kind) ||
    compareStrings(left.source, right.source) ||
    left.source_line - right.source_line
  );

  const document = {
    schema_version: "1.0",
    source_repository: sourceRepository,
    source_commit: sourceCommit.toLowerCase(),
    symbols,
  };
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, `${JSON.stringify(document, null, 2)}\n`, "utf8");
  return { output, symbolCount: symbols.length, countsByKind: symbols.reduce((counts, symbol) => {
    counts[symbol.kind] = (counts[symbol.kind] ?? 0) + 1;
    return counts;
  }, {}) };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const options = parseArguments(process.argv);
    const result = importJassdoc(options);
    console.log(`Imported ${result.symbolCount} symbols (${Object.entries(result.countsByKind).map(([kind, count]) => `${kind}=${count}`).join(", ")}) to ${result.output}`);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
