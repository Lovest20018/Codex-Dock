function (bindingName, resumeOnly) {
  "use strict";
  if (window.top !== window || !["app:", "file:"].includes(location.protocol)) return false;
  const previous = globalThis.__codexDockConnection;
  if (resumeOnly && (!previous || previous.version !== 2 || previous.busy !== 0 || previous.until > Date.now() && previous.enabled)) return false;
  previous?.stop?.();
  const pending = new Map(), threads = new Map(), internal = new WeakSet();
  let sequence = 0, stopped = false, operations = 0;
  const controller = {
    version: 2,
    get busy() { return operations + pending.size; },
    enabled: true, until: Date.now() + 15000,
    settle(id, response) {
      const request = pending.get(id);
      if (!request) return;
      pending.delete(id); clearTimeout(request.timer);
      response.ok ? request.resolve(response.value) : request.reject(new Error(response.error || "聊天接续未完成"));
    },
    stop() {
      stopped = true; controller.enabled = false;
      for (const request of pending.values()) { clearTimeout(request.timer); request.reject(new Error("Dock 接续已停止，请重试或重新应用。")); }
      pending.clear(); threads.clear();
    },
    async dispatch(manager, method, parameters, options, send) {
      operations++;
      try { return await dispatch(manager, method, parameters, options, send); }
      finally { operations--; }
    }
  };
  async function dispatch(manager, method, parameters, options, send) {
      if (!controller.enabled || stopped || Date.now() >= controller.until || manager.getHostId?.() !== "local" ||
          parameters?.hostId && parameters.hostId !== "local" || parameters?.host_id && parameters.host_id !== "local" ||
          !["thread/start", "thread/resume", "turn/start"].includes(method) || internal.has(parameters)) return send(method, parameters, options);
      if (method === "thread/start") {
        const state = await current();
        const response = await send(method, parameters, options);
        if (response?.thread?.id) threads.set(response.thread.id, { generation: state.generation, preferences: preferences(parameters, response), tail: Promise.resolve() });
        return response;
      }
      const id = parameters?.threadId;
      if (typeof id !== "string" || !/^[0-9a-f-]{36}$/i.test(id)) return send(method, parameters, options);
      let state = threads.get(id);
      if (!state) threads.set(id, state = { generation: null, preferences: {}, tail: Promise.resolve() });
      const predecessor = state.tail;
      let release;
      const ownTail = new Promise(resolve => { release = resolve; });
      state.tail = ownTail;
      let acquired = false;
      try {
        await wait(predecessor, options?.signal, 125000);
        acquired = true;
        return await prepare(manager, method, parameters, options, send, state);
      } catch (error) {
        void report("blocked"); throw error;
      } finally { if (acquired) release(); else void predecessor.finally(release); }
  }
  globalThis.__codexDockConnection = controller;
  function call(operation, detail = {}) {
    if (stopped || Date.now() >= controller.until || typeof globalThis[bindingName] !== "function") return Promise.reject(new Error("请保持 Dock 运行，或重新应用旧聊天接续。"));
    const id = ++sequence;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { pending.delete(id); reject(new Error("Dock 聊天接续响应超时，请重试。")); }, 12000);
      pending.set(id, { resolve, reject, timer });
      try { globalThis[bindingName](JSON.stringify({ id, operation, ...detail })); }
      catch (error) { clearTimeout(timer); pending.delete(id); reject(error); }
    });
  }
  const report = phase => call("report", { phase }).catch(() => {});
  async function current() {
    const state = await call("inspect");
    if (state.restartRequired) throw new Error(state.reason || "当前连接需要重新打开 Codex。通用应用入口会保留退出确认。");
    return state;
  }
  function preferences(parameters, cached, fallback = {}) {
    const collaboration = parameters?.collaborationMode?.settings;
    const own = (value, key) => value && Object.prototype.hasOwnProperty.call(value, key);
    const effort = own(collaboration, "reasoning_effort") ? collaboration.reasoning_effort :
      own(parameters, "effort") ? parameters.effort : parameters?.config?.model_reasoning_effort ?? cached?.reasoningEffort ?? fallback.effort;
    return {
      model: collaboration?.model ?? parameters?.model ?? cached?.model ?? fallback.model,
      effort,
      serviceTier: own(parameters, "serviceTier") ? parameters.serviceTier : own(cached, "serviceTier") ? cached.serviceTier : fallback.serviceTier
    };
  }
  function wait(promise, signal, milliseconds) {
    if (signal?.aborted) return Promise.reject(signal.reason || new Error("操作已取消"));
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => finish(reject, new Error("等待聊天连接超时，请重试。")), milliseconds);
      const abort = () => finish(reject, signal.reason || new Error("操作已取消"));
      function finish(action, value) { clearTimeout(timer); signal?.removeEventListener("abort", abort); action(value); }
      signal?.addEventListener("abort", abort, { once: true });
      Promise.resolve(promise).then(value => finish(resolve, value), error => finish(reject, error));
    });
  }
  async function prepare(manager, method, parameters, options, send, tracked) {
    const threadId = parameters.threadId;
    const target = await current();
    const nativeOptions = { priority: "critical", timeoutMs: 10000 };
    const read = () => send("thread/read", { threadId, includeTurns: false }, nativeOptions);
    const found = await read();
    const thread = found?.thread;
    if (!thread || thread.ephemeral === true) return send(method, parameters, options);
    const cached = await manager.getConversation?.(threadId);
    // The desktop marks its cache active optimistically before dispatching a new
    // turn. Native thread/read distinguishes that pending send from a live turn.
    if (thread.status?.type === "active") {
      if (tracked.generation !== target.generation || thread.modelProvider !== target.provider) void report("deferred");
      return send(method, parameters, options); // Native steering remains on the original active turn.
    }
    const providerChanged = thread.modelProvider !== target.provider;
    const rebind = providerChanged || tracked.generation !== target.generation;
    const selected = preferences(parameters, cached, tracked.preferences);
    if (!rebind) {
      const response = await send(method, parameters, options);
      tracked.preferences = preferences(parameters, response, selected); return response;
    }
    const config = await send("config/read", { cwd: parameters.cwd ?? thread.cwd, includeLayers: false }, nativeOptions);
    if ((config.config?.model_provider ?? "openai") !== target.provider) throw new Error("项目或 Profile 的供应商与 Dock 保存值不同，请先核对连接。历史保持不变。");
    if (target.provider === "openai" && config.config?.openai_base_url && target.baseUrl && config.config.openai_base_url !== target.baseUrl)
      throw new Error("项目的模型入口覆盖了 Dock 保存值，请先核对项目配置。历史保持不变。");
    if (thread.status?.type !== "notLoaded") {
      const before = await read();
      if (before.thread.status?.type === "active") { void report("deferred"); return send(method, parameters, options); }
      void report("waiting");
      await send("thread/unsubscribe", { threadId }, nativeOptions);
      const deadline = Date.now() + 110000;
      while (true) {
        if (options?.signal?.aborted || stopped) throw options?.signal?.reason || new Error("聊天接续已取消。");
        const state = await read();
        if (state.thread.status?.type === "active") throw new Error("聊天开始了新回复，已停止切换，请在回复完成后重试。");
        if (state.thread.status?.type === "notLoaded") break;
        if (Date.now() >= deadline) throw new Error("聊天仍被其他窗口使用，请关闭该聊天的其他窗口后重试。");
        await wait(new Promise(resolve => setTimeout(resolve, 250)), options?.signal, 1000);
      }
    }
    const latest = await current();
    if (latest.generation !== target.generation) throw new Error("恢复期间连接已改变，请重试。");
    if (providerChanged) await call("prepare", { threadId, sourceProvider: thread.modelProvider, targetProvider: target.provider, generation: target.generation });
    const resume = { threadId, modelProvider: target.provider };
    if (selected.model != null) resume.model = selected.model;
    if (selected.effort !== undefined) resume.config = { model_reasoning_effort: selected.effort };
    if (selected.serviceTier !== undefined) resume.serviceTier = selected.serviceTier;
    if (target.provider === "openai" && target.baseUrl) resume.config = { ...resume.config, openai_base_url: target.baseUrl };
    if (method === "thread/resume") {
      const request = { ...parameters, ...resume, config: { ...parameters.config, ...resume.config } };
      const response = await send(method, request, options);
      verify(response, threadId, target.provider);
      tracked.generation = target.generation; tracked.preferences = preferences(parameters, response, selected); void report("restored");
      return response;
    }
    if (typeof manager.resumeThread !== "function") throw new Error("客户端不支持安全刷新聊天缓存，未发送消息。请重新打开 Codex。");
    internal.add(resume);
    let response;
    try { response = await manager.resumeThread(resume); } finally { internal.delete(resume); }
    verify(response, threadId, target.provider);
    tracked.generation = target.generation; tracked.preferences = preferences(parameters, response, selected); void report("restored");
    const after = await current();
    if (after.generation !== target.generation) throw new Error("发送前连接已改变，请重试。");
    return send(method, parameters, options); // Exactly one original input, including its original UUID.
  }
  function verify(response, id, provider) {
    if (response?.thread?.id !== id || response.modelProvider !== provider) throw new Error("客户端恢复结果与目标连接不同，未继续发送。");
  }
  return true;
}
