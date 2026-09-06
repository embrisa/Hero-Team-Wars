import vm from 'node:vm';

// Deliberately small, fail-closed executable JASS subset. This is a source test
// harness, not a Warcraft VM: natives/handles/events are supplied by the test.
// No eval of JASS text, property access, native auto-stubs, or skipped statements.
const types = new Set(['nothing', 'integer', 'real', 'boolean', 'string', 'code',
  'unit', 'player', 'timer', 'trigger', 'group', 'force', 'rect', 'location',
  'effect', 'texttag', 'multiboard', 'multiboarditem', 'timerdialog', 'boolexpr']);
const id = '[A-Za-z_][A-Za-z0-9_]*';
const jsName = name => `__j_${name}`;
const defaults = type => type === 'boolean' ? false :
  type === 'integer' || type === 'real' ? 0 : null;
const numeric = type => type === 'integer' || type === 'real';
const assignable = (target, source) => target === source || target === 'real' && source === 'integer' ||
  source === 'null' && !['nothing', 'integer', 'real', 'boolean'].includes(target);
function requireType(target, source) {
  if (!assignable(target, source)) throw new Error(`Unsupported type conversion ${source} -> ${target}`);
}

export function rawcode(value) {
  if (!/^[\x20-\x7e]{4}$/.test(value)) throw new Error('Rawcode must contain four ASCII characters');
  return [...value].reduce((result, c) => result * 256 + c.charCodeAt(0), 0);
}

function tokens(text) {
  const result = [];
  while (text.length) {
    const whitespace = /^\s+/.exec(text);
    if (whitespace) { text = text.slice(whitespace[0].length); continue; }
    if (text.startsWith('//')) break;
    const token = /^(?:"(?:[^"\\\r\n]|\\["\\nrt])*"|'[\x20-\x26\x28-\x7e]{4}'|(?:\d+\.\d*|\.\d+|\d+)|[A-Za-z_][A-Za-z0-9_]*|==|!=|<=|>=|[+*/(),\[\]<>\-=])/.exec(text);
    if (!token) throw new Error(`Unsupported token: ${text}`);
    result.push(token[0]);
    text = text.slice(token[0].length);
  }
  return result;
}

const precedence = { or: 1, and: 2, '==': 3, '!=': 3, '<': 4, '>': 4,
  '<=': 4, '>=': 4, '+': 5, '-': 5, '*': 6, '/': 6 };
const operators = { or: '||', and: '&&', '==': '===', '!=': '!==' };

function expression(input, symbols, signatures) {
  const list = Array.isArray(input) ? input : tokens(input);
  let position = 0;
  const take = expected => {
    if (list[position++] !== expected) throw new Error(`Expected ${expected} in ${list.join(' ')}`);
  };
  function parse(minimum = 0) {
    const token = list[position++];
    let node;
    if (token === '(') { node = parse(); take(')'); }
    else if (token === 'not' || token === '-' || token === '+') {
      const operand = parse(7);
      if (token === 'not' ? operand.type !== 'boolean' : !numeric(operand.type)) throw new Error(`Unsupported unary operand ${operand.type}`);
      const code = `(${token === 'not' ? '!' : token}${operand.code})`;
      node = { code: operand.type === 'integer' ? `__integer(${code})` : code, type: token === 'not' ? 'boolean' : operand.type };
    } else if (token === 'function') {
      const name = list[position++];
      if (!signatures.has(name)) throw new Error(`Unknown callback ${name}`);
      if (signatures.get(name).arity !== 0) throw new Error(`Callback takes arguments: ${name}`);
      node = { code: jsName(name), type: 'code' };
    } else if (token === 'true' || token === 'false' || token === 'null') {
      node = { code: token, type: token === 'null' ? 'null' : 'boolean' };
    } else if (token?.startsWith('"')) {
      node = { code: JSON.stringify(JSON.parse(token)), type: 'string' };
    } else if (token?.startsWith("'")) {
      node = { code: String(rawcode(token.slice(1, -1))), type: 'integer' };
    } else if (/^(?:\d+\.?\d*|\.\d+)$/.test(token ?? '')) {
      if (!token.includes('.') && Number(token) > 2147483647) throw new Error('Unsupported integer literal outside signed 32-bit range');
      node = { code: String(Number(token)), type: token.includes('.') ? 'real' : 'integer' };
    } else if (new RegExp(`^${id}$`).test(token ?? '')) {
      if (list[position] === '(') {
        position++;
        const args = [];
        if (list[position] !== ')') {
          do {
            args.push(parse());
            if (list[position] !== ',') break;
            position++;
          } while (true);
        }
        take(')');
        const signature = signatures.get(token);
        if (!signature) throw new Error(`Unbound function/native ${token}`);
        if (signature.arity !== args.length) throw new Error(`Wrong argument count for ${token}`);
        signature.params?.forEach((param, index) => requireType(param.type, args[index].type));
        node = { code: `${jsName(token)}(${args.map(x => x.code).join(',')})`, type: signature.type, kind: 'call' };
      } else {
        const symbol = symbols.get(token);
        if (!symbol) throw new Error(`Undeclared variable ${token}`);
        let code = jsName(token);
        if (list[position] === '[') {
          if (!symbol.array) throw new Error(`Not an array: ${token}`);
          position++;
          const index = parse();
          if (index.type !== 'integer') throw new Error(`Noninteger array index: ${token}`);
          code += `[${index.code}]`;
          take(']');
        } else if (symbol.array) throw new Error(`Array requires index: ${token}`);
        node = { code, type: symbol.type };
      }
    } else throw new Error(`Unsupported expression ${list.join(' ')}`);
    while ((precedence[list[position]] ?? -1) >= minimum) {
      const op = list[position++];
      const rhs = parse(precedence[op] + 1);
      const integer = node.type === 'integer' && rhs.type === 'integer';
      const numbers = numeric(node.type) && numeric(rhs.type);
      if (op === 'and' || op === 'or') {
        if (node.type !== 'boolean' || rhs.type !== 'boolean') throw new Error('Logical operators require booleans');
      } else if (op === '==' || op === '!=') {
        if (!numbers && !assignable(node.type, rhs.type) && !assignable(rhs.type, node.type)) throw new Error('Incompatible comparison');
      } else if (!numbers && !(op === '+' && node.type === 'string' && rhs.type === 'string')) throw new Error('Unsupported arithmetic/comparison operands');
      const code = `(${node.code} ${operators[op] ?? op} ${rhs.code})`;
      node = { code: op === '/' && integer ? `__integerDivide(${node.code},${rhs.code})` : integer && precedence[op] >= 5 ? `__integer(${code})` : code,
        type: precedence[op] <= 4 ? 'boolean' :
          op === '+' && node.type === 'string' && rhs.type === 'string' ? 'string' : integer ? 'integer' : 'real' };
    }
    return node;
  }
  const result = parse();
  if (position !== list.length) throw new Error(`Unsupported expression tail: ${list.slice(position).join(' ')}`);
  return result;
}

function parseSources(sources) {
  const functions = new Map();
  for (const { path, source, only } of sources) {
    let current;
    for (const [offset, raw] of source.replace(/^\uFEFF/, '').split(/\r?\n/).entries()) {
      const line = tokens(raw).join(' ');
      if (!line) continue;
      const location = `${path}:${offset + 1}`;
      const header = new RegExp(`^function (${id}) takes (.+) returns (${id})$`).exec(line);
      if (header) {
        if (current) throw new Error(`${location}: Nested function`);
        const [, name, parameters, type] = header;
        if (functions.has(name)) throw new Error(`${location}: Duplicate function ${name}`);
        if (!types.has(type)) throw new Error(`${location}: Unsupported return type ${type}`);
        const params = parameters === 'nothing' ? [] : parameters.split(' , ').map(value => {
          const match = new RegExp(`^(${id}) (${id})$`).exec(value);
          if (!match || !types.has(match[1]) || match[1] === 'nothing') throw new Error(`${location}: Unsupported parameter ${value}`);
          return { type: match[1], name: match[2] };
        });
        current = { name, params, type, location, lines: [] };
        functions.set(name, current);
      } else if (line === 'endfunction') {
        if (!current) throw new Error(`${location}: Unexpected endfunction`);
        current = undefined;
      } else if (current) current.lines.push({ line, location });
      else throw new Error(`${location}: Unsupported top-level syntax: ${line}`);
    }
    if (current) throw new Error(`${current.location}: Missing endfunction`);
    if (only) {
      for (const name of only) if (!functions.has(name)) throw new Error(`${path}: Missing selected function ${name}`);
      for (const [name, fn] of functions) if (fn.location.startsWith(`${path}:`) && !only.includes(name)) functions.delete(name);
    }
  }
  return functions;
}

function compileFunction(fn, globals, signatures) {
  const symbols = new Map(globals);
  const locals = new Set();
  for (const param of fn.params) {
    if (locals.has(param.name)) throw new Error(`${fn.location}: Duplicate parameter`);
    locals.add(param.name);
    symbols.set(param.name, param);
  }
  const code = [`function ${jsName(fn.name)}(${fn.params.map(p => jsName(p.name)).join(',')}) { __step(); __functionCalls.push(${JSON.stringify(fn.name)});`];
  const blocks = [];
  let statements = false;
  for (const { line, location } of fn.lines) {
    try {
      let match;
      const expr = value => expression(value, symbols, signatures);
      if ((match = new RegExp(`^local (${id}) (${id})(?: = (.+))?$`).exec(line))) {
        const [, type, name, initial] = match;
        if (statements || !types.has(type) || type === 'nothing' || locals.has(name)) throw new Error('Unsupported/duplicate local declaration');
        if (initial) requireType(type, expr(initial).type);
        const value = initial ? expr(initial).code : JSON.stringify(defaults(type));
        locals.add(name);
        symbols.set(name, { type });
        code.push(`let ${jsName(name)} = ${value};`);
        continue;
      }
      statements = true;
      if ((match = /^set (.+) = (.+)$/.exec(line))) {
        const lhs = tokens(match[1]);
        if (!new RegExp(`^${id}$`).test(lhs[0]) ||
          !(lhs.length === 1 || lhs[1] === '[' && lhs.at(-1) === ']')) throw new Error('Unsupported assignment target');
        if (symbols.get(lhs[0])?.constant) throw new Error('Assignment to constant');
        requireType(expr(lhs).type, expr(match[2]).type);
        code.push(`${expr(lhs).code} = ${expr(match[2]).code};`);
      } else if ((match = /^call (.+)$/.exec(line))) {
        if (!new RegExp(`^${id} \\(`).test(match[1])) throw new Error('Call requires a function');
        const call = expr(match[1]);
        if (call.kind !== 'call') throw new Error('Call requires exactly one function invocation');
        code.push(`${call.code};`);
      } else if ((match = /^if (.+) then$/.exec(line))) {
        if (expr(match[1]).type !== 'boolean') throw new Error('Nonboolean condition');
        blocks.push({ kind: 'if', otherwise: false });
        code.push(`if (${expr(match[1]).code}) {`);
      } else if ((match = /^elseif (.+) then$/.exec(line))) {
        if (blocks.at(-1)?.kind !== 'if' || blocks.at(-1).otherwise) throw new Error('Unexpected elseif');
        if (expr(match[1]).type !== 'boolean') throw new Error('Nonboolean condition');
        code.push(`} else if (${expr(match[1]).code}) {`);
      } else if (line === 'else') {
        if (blocks.at(-1)?.kind !== 'if' || blocks.at(-1).otherwise) throw new Error('Unexpected else');
        blocks.at(-1).otherwise = true;
        code.push('} else {');
      } else if (line === 'endif') {
        if (blocks.pop()?.kind !== 'if') throw new Error('Unexpected endif');
        code.push('}');
      } else if (line === 'loop') {
        blocks.push({ kind: 'loop' });
        code.push('while (true) { __step();');
      } else if ((match = /^exitwhen (.+)$/.exec(line))) {
        if (!blocks.some(b => b.kind === 'loop')) throw new Error('exitwhen outside loop');
        if (expr(match[1]).type !== 'boolean') throw new Error('Nonboolean exitwhen');
        code.push(`if (${expr(match[1]).code}) break;`);
      } else if (line === 'endloop') {
        if (blocks.pop()?.kind !== 'loop') throw new Error('Unexpected endloop');
        code.push('}');
      } else if (line === 'return' || line.startsWith('return ')) {
        const value = line.slice(7);
        if ((fn.type === 'nothing') === Boolean(value)) throw new Error('Return value does not match function');
        if (value) requireType(fn.type, expr(value).type);
        code.push(value ? `return ${expr(value).code};` : 'return;');
      } else throw new Error(`Unsupported statement: ${line}`);
    } catch (error) { throw new Error(`${location}: ${error.message}`, { cause: error }); }
  }
  if (blocks.length) throw new Error(`${fn.location}: Unclosed control flow`);
  if (fn.type !== 'nothing') code.push(`throw new Error(${JSON.stringify(`${fn.name}: missing return`)});`);
  code.push('}');
  return code.join('\n');
}

function jassArray(type) {
  const values = Object.create(null);
  const valid = key => /^(0|[1-9]\d*)$/.test(String(key)) && Number(key) < 32768;
  return new Proxy(values, {
    get(target, key) {
      if (!valid(key)) throw new Error(`Unsupported JASS array index: ${String(key)}`);
      return Object.hasOwn(target, key) ? target[key] : defaults(type);
    },
    set(target, key, value) {
      if (!valid(key)) throw new Error(`Unsupported JASS array index: ${String(key)}`);
      target[key] = value;
      return true;
    },
  });
}

// Each native is explicit: { type: JASS return type, arity, fn }. HTW_* mocks
// are forbidden so tests cannot quietly substitute a second gameplay model.
export function createJassRuntime({ sources, globals = [], natives = {}, timeout = 100, maxSteps = 10000 }) {
  if (!Number.isSafeInteger(maxSteps) || maxSteps < 1 || !Number.isSafeInteger(timeout) || timeout < 1) throw new Error('Invalid execution bounds');
  const functions = parseSources(sources);
  const symbols = new Map();
  const signatures = new Map([...functions].map(([name, fn]) => [name, { type: fn.type, arity: fn.params.length, params: fn.params }]));
  const sandbox = Object.create(null);
  for (const global of globals) {
    if (!new RegExp(`^${id}$`).test(global.name) || !types.has(global.type) || global.type === 'nothing') throw new Error(`Unsupported global ${global.name}`);
    if (symbols.has(global.name)) throw new Error(`Duplicate global ${global.name}`);
    symbols.set(global.name, global);
    sandbox[jsName(global.name)] = global.array ? jassArray(global.type) : global.initial ?? defaults(global.type);
  }
  const nativeCalls = [];
  for (const [name, native] of Object.entries(natives)) {
    if (name.startsWith('HTW_') || functions.has(name)) throw new Error(`Cannot mock repository function ${name}`);
    if (!new RegExp(`^${id}$`).test(name) || !types.has(native.type) || !Number.isInteger(native.arity) || typeof native.fn !== 'function') throw new Error(`Invalid native ${name}`);
    signatures.set(name, native);
    sandbox[jsName(name)] = (...args) => {
      nativeCalls.push({ name, args: [...args] });
      return native.fn(...args);
    };
  }
  const context = vm.createContext(sandbox, { codeGeneration: { strings: false, wasm: false } });
  const functionCalls = [];
  sandbox.__functionCalls = functionCalls;
  const compiled = [...functions.values()].map(fn => compileFunction(fn, symbols, signatures)).join('\n');
  new vm.Script(`"use strict"; let __steps = 0; function __step() { if (++__steps > ${maxSteps}) throw new Error("JASS execution step limit"); }
    // Overflow is outside this subset; fail instead of using JavaScript doubles
    // as an accidental replacement for Warcraft signed integer behavior.
    function __integer(value) { if (!Number.isSafeInteger(value) || value < -2147483648 || value > 2147483647) throw new Error("Unsupported JASS integer overflow"); return value; }
    function __integerDivide(a,b) { if (b === 0) throw new Error("JASS division by zero"); return __integer(Math.trunc(a/b)); }
    ${compiled}`, { filename: 'repository-jass.mocked.js' }).runInContext(context, { timeout });
  const state = new Proxy(Object.create(null), {
    get(_target, name) {
      if (!symbols.has(name)) throw new Error(`Unknown global ${String(name)}`);
      return sandbox[jsName(name)];
    },
    set(_target, name, value) {
      if (!symbols.has(name)) throw new Error(`Unknown global ${String(name)}`);
      sandbox[jsName(name)] = value;
      return true;
    },
  });
  function invoke(callback, args = []) {
    if (typeof callback !== 'function') throw new Error('Missing JASS function/callback');
    sandbox.__callback = callback;
    sandbox.__args = args;
    try {
      return new vm.Script('__steps = 0; __callback(...__args)', { filename: 'jass-test-entry.js' }).runInContext(context, { timeout });
    } finally { delete sandbox.__callback; delete sandbox.__args; }
  }
  return { state, nativeCalls, functionCalls, functions: [...functions.keys()],
    call(name, ...args) {
      if (!functions.has(name)) throw new Error(`Missing repository JASS function ${name}`);
      if (args.length !== signatures.get(name).arity) throw new Error(`Wrong argument count for ${name}`);
      return invoke(sandbox[jsName(name)], args);
    },
    invoke,
  };
}
