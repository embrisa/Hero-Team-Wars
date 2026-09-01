import { WorkerClient } from "../transport/worker-client.js";

/**
 * Thin adapter for the worker's canonical jassdoc-backed knowledge operations.
 * The MCP layer deliberately does not carry signatures or a duplicate API list.
 */
export class JassService {
  public constructor(private readonly worker: WorkerClient) {}

  public lookup(name: string, correlationId: string): Promise<Record<string, unknown>> {
    return this.worker.request<Record<string, unknown>>("jass_lookup", { name }, correlationId);
  }

  public search(query: string, limit: number, correlationId: string): Promise<Record<string, unknown>> {
    return this.worker.request<Record<string, unknown>>("jass_search", { query, limit }, correlationId);
  }

  public validateCall(functionName: string, argumentsList: string[], localSource: string | undefined, correlationId: string): Promise<Record<string, unknown>> {
    return this.worker.request<Record<string, unknown>>("jass_validate_call", {
      function: functionName,
      arguments: argumentsList,
      ...(localSource === undefined ? {} : { local_source: localSource })
    }, correlationId);
  }

  public validateSource(source: string, contextSource: string | undefined, correlationId: string): Promise<Record<string, unknown>> {
    return this.worker.request<Record<string, unknown>>("jass_validate_source", {
      source,
      ...(contextSource === undefined ? {} : { context_source: contextSource })
    }, correlationId);
  }
}
