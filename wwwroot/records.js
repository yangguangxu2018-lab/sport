const filters = document.querySelector("#filters");
const recordsBox = document.querySelector("#records");
const statusBox = document.querySelector("#status");
const sportFilter = document.querySelector("#sportFilter");
const weekSummaryBox = document.querySelector("#weekSummary");
const sessionGreeting = document.querySelector("#sessionGreeting");
const logoutButton = document.querySelector("#logoutButton");
const passwordForm = document.querySelector("#passwordForm");
const passwordStatusBox = document.querySelector("#passwordStatus");
const userFilterWrap = document.querySelector("#userFilterWrap");
const reviewStatusWrap = document.querySelector("#reviewStatusWrap");
const sportAdminLink = document.querySelector("#sportAdminLink");
const pageEyebrow = document.querySelector("#pageEyebrow");
const pageTitle = document.querySelector("#pageTitle");
const userSelect = filters.elements.user;

let session = null;

function redirectToLogin() {
  window.location.replace("/login.html");
}

function setStatus(message, kind = "") {
  statusBox.textContent = message;
  statusBox.className = `status ${kind}`.trim();
}

function setPasswordStatus(message, kind = "") {
  passwordStatusBox.textContent = message;
  passwordStatusBox.className = `status ${kind}`.trim();
}

function formatAverage(value) {
  return Number.isInteger(value) ? value.toString() : value.toFixed(2);
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
    ? `管理员 ${session.displayName} 已登录，可以按最新提交时间审核运行记录。`
    : `${session.displayName} 已登录，只显示自己的历史记录。`;

  if (!session.isAdmin) {
    userFilterWrap.hidden = true;
    userSelect.value = session.displayName;
    reviewStatusWrap.hidden = true;
    sportAdminLink.hidden = true;
    pageEyebrow.textContent = "历史记录";
    pageTitle.textContent = "本周总分和历史记录";
  } else {
    reviewStatusWrap.hidden = false;
    sportAdminLink.hidden = false;
    pageEyebrow.textContent = "记录审核";
    pageTitle.textContent = "按时间倒序审核运行记录";
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
    ? `<span>管理员 ${record.scoredBy} 评分</span><span>${record.scoredAt}</span>`
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

async function loadRecords() {
  setStatus("正在读取记录...");
  recordsBox.innerHTML = "";

  try {
    const params = buildParams();
    const data = await fetchJson(`/api/records?${params.toString()}`);
    const orderedRecords = [...data.records].sort((left, right) =>
      right.submittedAt.localeCompare(left.submittedAt) || right.id - left.id);

    renderWeeklySummary(data.weeklySummary);

    if (orderedRecords.length === 0) {
      setStatus("还没有符合条件的记录。");
      return;
    }

    setStatus(`共 ${orderedRecords.length} 条记录，已按提交时间倒序排列。`);
    recordsBox.innerHTML = orderedRecords.map(renderRecord).join("");
  } catch (error) {
    setStatus(error.message, "error");
  }
}

logoutButton.addEventListener("click", async () => {
  logoutButton.disabled = true;

  try {
    await fetch("/api/auth/logout", { method: "POST" });
  } finally {
    redirectToLogin();
  }
});

passwordForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const button = passwordForm.querySelector("button");
  const formData = new FormData(passwordForm);
  const currentPassword = String(formData.get("currentPassword") || "");
  const newPassword = String(formData.get("newPassword") || "");
  const confirmPassword = String(formData.get("confirmPassword") || "");

  if (newPassword !== confirmPassword) {
    setPasswordStatus("两次输入的新密码不一致。", "error");
    return;
  }

  button.disabled = true;
  button.textContent = "修改中";

  try {
    const data = await fetchJson("/api/auth/change-password", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ currentPassword, newPassword })
    });

    passwordForm.reset();
    setPasswordStatus(data.message || "密码修改成功。", "ok");
  } catch (error) {
    setPasswordStatus(error.message, "error");
  } finally {
    button.disabled = false;
    button.textContent = "修改密码";
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

(async () => {
  try {
    await ensureSession();
    setPasswordStatus("");
    await loadSports();
    await loadRecords();
  } catch (error) {
    setStatus(error.message, "error");
  }
})();
