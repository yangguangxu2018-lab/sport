const taskList = document.querySelector("#taskList");
const statusBox = document.querySelector("#status");
const sessionGreeting = document.querySelector("#sessionGreeting");
const sessionChip = document.querySelector("#sessionChip");
const scoreboardBox = document.querySelector("#scoreboard");
const logoutButton = document.querySelector("#logoutButton");
const passwordForm = document.querySelector("#passwordForm");
const passwordStatusBox = document.querySelector("#passwordStatus");

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

function formatGoal(task) {
  return `${task.targetValue}${task.unit}`;
}

function formatAverage(value) {
  return Number.isInteger(value) ? value.toString() : value.toFixed(2);
}

function renderSession() {
  sessionGreeting.textContent = `${session.displayName} 已登录，每天会随机生成 5 项运动。`;
  sessionChip.innerHTML = `
    <span class="chip-label">当前账号</span>
    <strong>${session.displayName}</strong>
    <span>${session.username}</span>
  `;
}

function renderScoreboardCard(item) {
  const isCurrentUser = item.userName === session.displayName;

  return `
    <article class="summary-card ${isCurrentUser ? "summary-card-active" : ""}">
      <p class="summary-label">${item.userName} 本周得分</p>
      <strong>${item.weeklySummary.totalScore}</strong>
      <span>平均分 ${formatAverage(item.weeklySummary.averageScore)}</span>
    </article>
  `;
}

async function loadScoreboard() {
  scoreboardBox.innerHTML = `
    <article class="summary-card">
      <p class="summary-label">本周得分榜</p>
      <strong>读取中...</strong>
    </article>
  `;

  try {
    const data = await fetchJson("/api/home-scoreboard");
    scoreboardBox.innerHTML = data.summaries.map(renderScoreboardCard).join("");
  } catch (error) {
    scoreboardBox.innerHTML = `
      <article class="summary-card">
        <p class="summary-label">本周得分榜</p>
        <strong>读取失败</strong>
        <span>${error.message}</span>
      </article>
    `;
  }
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
  if (session.isAdmin) {
    window.location.replace("/records.html");
    return false;
  }

  renderSession();
  return true;
}

function renderTask(task) {
  if (task.recordId) {
    return `
      <article class="task-card task-card-done">
        <div class="task-main">
          <div>
            <h2 class="task-title">${task.sportName}</h2>
            <p class="meta">
              <span>目标 ${formatGoal(task)}</span>
              <span>已完成 ${task.actualValue}${task.unit}</span>
            </p>
          </div>
          <span class="pill ok-pill">已提交</span>
        </div>
        <div class="submitted-note">
          <span>${task.submittedAt}</span>
          <span>${task.score === null ? "等待管理员评分" : `评分 ${task.score} 分`}</span>
        </div>
      </article>
    `;
  }

  return `
    <article class="task-card" data-task-id="${task.id}">
      <div class="task-main">
        <div>
          <h2 class="task-title">${task.sportName}</h2>
          <p class="meta">目标 <span class="target">${formatGoal(task)}</span></p>
        </div>
      </div>
      <form class="submit-row">
        <input type="number" name="actualValue" inputmode="numeric" min="1" step="1" placeholder="实际完成${task.unit}" required>
        <span>${task.unit}</span>
        <button type="submit">提交</button>
      </form>
    </article>
  `;
}

async function loadTasks() {
  setStatus("正在读取今日运动...");
  taskList.innerHTML = "";

  try {
    const data = await fetchJson("/api/tasks/today");
    const finishedCount = data.tasks.filter((task) => task.recordId).length;

    setStatus(`${data.date}，${data.user} 今天随机出的 5 项运动。已完成 ${finishedCount} 项。`);
    taskList.innerHTML = data.tasks.map(renderTask).join("");
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

taskList.addEventListener("submit", async (event) => {
  event.preventDefault();

  const form = event.target;
  const card = form.closest(".task-card");
  const button = form.querySelector("button");
  const actualValue = Number(new FormData(form).get("actualValue"));

  button.disabled = true;
  button.textContent = "提交中";

  try {
    const data = await fetchJson("/api/records", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        taskId: Number(card.dataset.taskId),
        actualValue
      })
    });

    setStatus(`${data.userName} 已记录 ${data.sportName}：${data.actualValue}${data.unit}。`, "ok");
    await loadTasks();
  } catch (error) {
    setStatus(error.message, "error");
  } finally {
    button.disabled = false;
    button.textContent = "提交";
  }
});

(async () => {
  try {
    const ready = await ensureSession();
    if (ready) {
      setPasswordStatus("");
      await loadScoreboard();
      await loadTasks();
    }
  } catch (error) {
    setStatus(error.message, "error");
  }
})();
