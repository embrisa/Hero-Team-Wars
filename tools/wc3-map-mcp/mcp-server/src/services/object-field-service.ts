import type { WorkerClient } from "../transport/worker-client.js";

/** Metadata stays in the engine's embedded catalog, never duplicated in TS. */
export class ObjectFieldService {
  public constructor(private readonly worker: WorkerClient) {}
  public lookup(input: Record<string, unknown>, correlationId: string): Promise<Record<string, unknown>> {
    return this.worker.request("object_field_lookup", input, correlationId);
  }
  public search(input: Record<string, unknown>, correlationId: string): Promise<Record<string, unknown>> {
    return this.worker.request("object_field_search", input, correlationId);
  }
}
