const filters = document.querySelector("#filters");
const recordsBox = document.querySelector("#records");
const statusBox = document.querySelector("#status");
const sportFilter = document.querySelector("#sportFilter");
const weekSummaryBox = document.querySelector("#weekSummary");
const sessionGreeting = document.querySelector("#sessionGreeting");
const logoutButton = document.querySelector("#logoutButton");
const userFilterWrap = document.querySelector("#userFilterWrap");
const userSelect = filters.elements.user;
const sportAdminSection = document.querySelector("#sportAdminSection");
const sportAdminStatusBox = document.querySelector("#sportAdminStatus");
const sportAdminList = document.querySelector("#sportAdminList");
const createSportForm = document.querySelector("#createSportForm");

let session = null;

const kindLabels = {
  Count: "次数",
  TimeMinutes: "分钟",
  TimeSeconds: "秒"
};

function redirectToLogin() {
  window.location.replace("/login.html");
}

function setStatus(message, kind = "") {
  statusBox.textContent = message;
  statusBox.className = `status ${kind}`.trim();
}

function setSportAdminStatus(message, kind = "") {
  sportAdminStatusBox.textContent = message;
  sportAdminStatusBox.className = `status ${kind}`.trim();
}

function formatAverage(value) {
  return Number.isInteger(value) ? value.toString() : value.toFixed(2);
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (char) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#39;"
  }[char]));
}

function renderWeeklySummary(summary) {
  weekSummaryBox.innerHTML = `
    <article class="summary-card">
      <p class="summary-label">本周总分</p>
      <strong>${summary.totalScore}</strong>
    </article>
    <article class="summary-card">
      <p class="summary-label">本周平均分</p>
      <strong>${formatAverage(summary.averageScore)}</strong>
    </article>
    <article class="summary-card">
      <p class="summary-label">统计范围</p>
      <strong>${summary.startDate} 至 ${summary.endDate}</strong>
      <span>已过 ${summary.daysElapsed} 天</span>
    </article>
  `;
}

function renderKindOptions(selectedKind) {
  return Object.entries(kindLabels)
    .map(([value, label]) => `<option value="${value}" ${selectedKind === value ? "selected" : ""}>${label}</option>`)
    .join("");
}

function renderSportEditor(sport) {
  return `
    <form class="sport-editor sport-editor-update" data-sport-id="${sport.id}">
      <div class="sport-editor-grid">
        <label>
          项目名称
          <input type="text" name="name" value="${escapeHtml(sport.name)}" required>
        </label>
        <label>
          类型
          <select name="kind">${renderKindOptions(sport.kind)}</select>
        </label>
        <label>
          最小随机值
          <input type="number" name="minTarget" min="1" step="1" value="${sport.minTarget}" required>
        </label>
        <label>
          最大随机值
          <input type="number" name="maxTarget" min="1" step="1" value="${sport.maxTarget}" required>
        </label>
      </div>
      <div class="sport-editor-actions">
        <span class="meta-note">当前单位：${sport.unit}，随机范围 ${sport.minTarget} - ${sport.maxTarget}</span>
        <button type="submit">保存项目</button>
      </div>
    </form>
  `;
}

async function fetchJson(url, options = {}) {
  const response = await fetch(url, options);

  if (response.status === 401) {
    redirectToLogin();
    throw new Error("登录已失效，请重新登录。");
  }

  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(data.message || "请求失败");
  }

  return data;
}

async function ensureSession() {
  session = await fetchJson("/api/auth/me");
  sessionGreeting.textContent = session.isAdmin
    ? `管理员 ${session.displayName} 已登录，可以查看、评分，也可以修改运动项目。`
    : `${session.displayName} 已登录，只显示自己的历史记录。`;

  if (!session.isAdmin) {
    userFilterWrap.hidden = true;
    userSelect.value = session.displayName;
    sportAdminSection.hidden = true;
  } else {
    sportAdminSection.hidden = false;
  }
}

async function loadSports() {
  const currentValue = sportFilter.value;
  const sports = await fetchJson("/api/sports");
  sportFilter.innerHTML = `
    <option value="">全部</option>
    ${sports.map((sport) => `<option value="${sport.name}">${sport.name}</option>`).join("")}
  `;

  if (currentValue && sports.some((sport) => sport.name === currentValue)) {
    sportFilter.value = currentValue;
  }
}

function buildParams() {
  const formData = new FormData(filters);
  const params = new URLSearchParams();

  for (const [key, value] of formData.entries()) {
    if (value) {
      params.set(key, value);
    }
  }

  return params;
}

function renderScoreEditor(record) {
  const options = ['<option value="">未评分</option>'];
  for (let score = 0; score <= 10; score += 1) {
    const selected = record.score === score ? "selected" : "";
    options.push(`<option value="${score}" ${selected}>${score} 分</option>`);
  }

  return `
    <form class="score-form" data-record-id="${record.id}">
      <label class="score-label">
        评分
        <select name="score">${options.join("")}</select>
      </label>
      <button type="submit">保存评分</button>
    </form>
  `;
}

function renderRecord(record) {
  const scoreText = record.score === null ? "未评分" : `${record.score} 分`;
  const scoredMeta = record.scoredBy
    ? `<span>评分人 ${record.scoredBy}</span><span>${record.scoredAt}</span>`
    : `<span>等待管理员评分</span>`;

  return `
    <article class="record-card">
      <div class="record-head">
        <div>
          <h2 class="record-title">${record.userName} · ${record.sportName}</h2>
          <p class="meta">
            <span>目标 ${record.targetValue}${record.unit}</span>
            <span>实际 ${record.actualValue}${record.unit}</span>
            <span>任务日期 ${record.taskDate}</span>
            <span>提交时间 ${record.submittedAt}</span>
          </p>
        </div>
        <span class="pill score-pill">${scoreText}</span>
      </div>
      <div class="record-footer">
        <p class="meta">${scoredMeta}</p>
        ${session.isAdmin ? renderScoreEditor(record) : ""}
      </div>
    </article>
  `;
}

function readSportPayload(form) {
  const formData = new FormData(form);
  return {
    name: String(formData.get("name") || "").trim(),
    kind: String(formData.get("kind") || "Count"),
    minTarget: Number(formData.get("minTarget")),
    maxTarget: Number(formData.get("maxTarget"))
  };
}

async function loadAdminSports() {
  if (!session.isAdmin) {
    return;
  }

  setSportAdminStatus("正在读取运动项目...");
  sportAdminList.innerHTML = "";

  try {
    const sports = await fetchJson("/api/admin/sports");
    setSportAdminStatus(`共 ${sports.length} 个可随机使用的运动项目。`, "ok");
    sportAdminList.innerHTML = sports.map(renderSportEditor).join("");
  } catch (error) {
    setSportAdminStatus(error.message, "error");
  }
}

async function loadRecords() {
  setStatus("正在读取记录...");
  recordsBox.innerHTML = "";

  try {
    const params = buildParams();
    const data = await fetchJson(`/api/records?${params.toString()}`);

    renderWeeklySummary(data.weeklySummary);

    if (data.records.length === 0) {
      setStatus("还没有符合条件的记录。");
      return;
    }

    setStatus(`共 ${data.records.length} 条记录。`);
    recordsBox.innerHTML = data.records.map(renderRecord).join("");
  } catch (error) {
    setStatus(error.message, "error");
  }
}

async function refreshAdminData() {
  await loadSports();
  await loadAdminSports();
  await loadRecords();
}

logoutButton.addEventListener("click", async () => {
  logoutButton.disabled = true;

  try {
    await fetch("/api/auth/logout", { method: "POST" });
  } finally {
    redirectToLogin();
  }
});

filters.addEventListener("submit", (event) => {
  event.preventDefault();
  loadRecords();
});

recordsBox.addEventListener("submit", async (event) => {
  const form = event.target;
  if (!form.classList.contains("score-form")) {
    return;
  }

  event.preventDefault();

  const button = form.querySelector("button");
  const rawScore = new FormData(form).get("score");
  const score = rawScore === "" ? null : Number(rawScore);

  button.disabled = true;
  button.textContent = "保存中";

  try {
    const recordId = Number(form.dataset.recordId);
    await fetchJson(`/api/records/${recordId}/score`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ score })
    });

    setStatus("评分已保存。", "ok");
    await loadRecords();
  } catch (error) {
    setStatus(error.message, "error");
  } finally {
    button.disabled = false;
    button.textContent = "保存评分";
  }
});

createSportForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const button = createSportForm.querySelector("button");
  const payload = readSportPayload(createSportForm);

  button.disabled = true;
  button.textContent = "新增中";

  try {
    await fetchJson("/api/admin/sports", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });

    createSportForm.reset();
    createSportForm.elements.kind.value = "Count";
    setSportAdminStatus("运动项目已新增。", "ok");
    await refreshAdminData();
  } catch (error) {
    setSportAdminStatus(error.message, "error");
  } finally {
    button.disabled = false;
    button.textContent = "新增项目";
  }
});

sportAdminList.addEventListener("submit", async (event) => {
  const form = event.target;
  if (!form.classList.contains("sport-editor-update")) {
    return;
  }

  event.preventDefault();

  const button = form.querySelector("button");
  const payload = readSportPayload(form);
  const sportId = Number(form.dataset.sportId);

  button.disabled = true;
  button.textContent = "保存中";

  try {
    await fetchJson(`/api/admin/sports/${sportId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });

    setSportAdminStatus("运动项目已更新。", "ok");
    await refreshAdminData();
  } catch (error) {
    setSportAdminStatus(error.message, "error");
  } finally {
    button.disabled = false;
    button.textContent = "保存项目";
  }
});

(async () => {
  try {
    await ensureSession();
    await loadSports();
    if (session.isAdmin) {
      await loadAdminSports();
    }
    await loadRecords();
  } catch (error) {
    setStatus(error.message, "error");
  }
})();
