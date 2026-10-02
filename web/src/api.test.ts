import { describe, expect, it } from "vitest";
import { nextActions } from "./api";
describe("PO workflow", () => {
  it("mirrors the backend state machine", () => {
    expect(nextActions.CREATED).toEqual(["approve", "cancel"]);
    expect(nextActions.SHIPPED).toEqual(["receive"]);
    expect(nextActions.RECEIVED).toEqual([]);
  });
});
