// Requests to the app (ApiHost.cs) over WebView2 web messages.
// Each call posts { id, method, args } and resolves with the reply that carries the same id.

// The app can also send messages nobody asked for, { event, ... }, such as a race turning up in
// the results folder; onEvent subscribes to them.

const pending = new Map();
const listeners = new Map();
let nextId = 1;

window.chrome?.webview?.addEventListener("message", (event) => {
  const reply = event.data;
  if (reply.event) {
    for (const listener of listeners.get(reply.event) ?? []) listener(reply);
    return;
  }
  const waiting = pending.get(reply.id);
  if (!waiting) return;
  pending.delete(reply.id);
  if (reply.ok) waiting.resolve(reply.result);
  else waiting.reject(Object.assign(new Error(reply.error), { phrase: reply.phrase ?? null }));
});

/** Subscribes to an app event; returns a function that unsubscribes. */
export function onEvent(name, listener) {
  if (!listeners.has(name)) listeners.set(name, new Set());
  listeners.get(name).add(listener);
  return () => listeners.get(name).delete(listener);
}

export function call(method, args = {}) {
  if (!window.chrome?.webview) return Promise.reject(new Error("Not running inside the app."));
  const id = nextId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    window.chrome.webview.postMessage({ id, method, args });
  });
}
