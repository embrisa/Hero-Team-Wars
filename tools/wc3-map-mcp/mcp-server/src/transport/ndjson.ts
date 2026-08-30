export function encodeNdjson(value: unknown): string {
  return `${JSON.stringify(value)}\n`;
}

export function parseNdjsonLine(line: string): unknown {
  return JSON.parse(line) as unknown;
}
