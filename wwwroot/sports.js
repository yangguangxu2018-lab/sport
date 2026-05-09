const sessionGreeting = document.querySelector("#sessionGreeting");
const logoutButton = document.querySelector("#logoutButton");
const passwordForm = document.querySelector("#passwordForm");
const passwordStatusBox = document.querySelector("#passwordStatus");
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

function setPasswordStatus(message, kind = "") {
  passwordStatusBox.textContent = message;
  passwordStatusBox.className = `status ${kind}`.trim();
}

function setSportAdminStatus(message, kind = "") {
  sportAdminStatusBox.textContent = message;
  sportAdminStatusBox.className = `status ${kind}`.trim();
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

function readSportPayload(form) {
  const formData = new FormData(form);
  return {
    name: String(formData.get("name") || "").trim(),
    kind: String(formData.get("kind") || "Count"),
    minTarget: Number(formData.get("minTarget")),
    maxTarget: Number(formData.get("maxTarget"))
  };
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
  if (!session.isAdmin) {
    window.location.replace("/records.html");
    return false;
  }

  sessionGreeting.textContent = `管理员 ${session.displayName} 已登录，可以维护运动项目。`;
  return true;
}

async function loadAdminSports() {
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
    await loadAdminSports();
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
    await loadAdminSports();
  } catch (error) {
    setSportAdminStatus(error.message, "error");
  } finally {
    button.disabled = false;
    button.textContent = "保存项目";
  }
});

(async () => {
  try {
    const ready = await ensureSession();
    if (ready) {
      setPasswordStatus("");
      await loadAdminSports();
    }
  } catch (error) {
    setSportAdminStatus(error.message, "error");
  }
})();
