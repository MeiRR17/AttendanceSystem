"use strict";
(() => {
  const byId = id => document.getElementById(id);
  const names = {
    OnTime: "בזמן", Late: "איחור", Excused: "פטור",
    Unclassified: "לא מסווג", Invalid: "לא תקין", Absent: "נעדר"
  };
  const viewTitles = {
    overview: ["סקירה כללית", "תמונת מצב של אירועים מסווגים. כל הנתונים להמחשה בלבד."],
    events: ["אירועי נוכחות", "רשומות ומידע מתועד ללא פרטים אישיים."],
    audit: ["יומן תיקונים", "תיקונים מתועדים בגרסאות נפרדות, ללא שינוי אירוע המקור."],
    system: ["מצב מערכת", "מידע על אבטחה, תצורה והגבלות הפעלה."]
  };
  const sample = [
    ["1001","2026-11-09T06:59:00Z","OnTime",0],
    ["1002","2026-11-09T07:12:00Z","Late",0],
    ["1003","2026-11-09T07:28:00Z","OnTime",0],
    ["1004","2026-11-09T07:41:00Z","Excused",1],
    ["1005","2026-11-09T07:53:00Z","Late",0],
    ["1006","2026-11-09T08:11:00Z","OnTime",0],
    ["1007","2026-11-09T08:19:00Z","Unclassified",0],
    ["1008","2026-11-09T08:31:00Z","OnTime",0],
    ["1009","2026-11-09T08:43:00Z","Late",0],
    ["1010","2026-11-09T09:02:00Z","OnTime",0],
    ["1011","2026-11-09T09:14:00Z","OnTime",0],
    ["1012","2026-11-09T09:25:00Z","OnTime",0]
  ].map(([id, occurredAtUtc, status, revision]) =>
    ({eventId: Number(id), occurredAtUtc, status, revision}));
  const state = {
    mode: "demo", view: "overview", role: "Administrator", token: null,
    events: sample.map(x => ({...x})), counts: {}, audit: [{
      eventId: 1004, beforeStatus: "Late", afterStatus: "Excused",
      reasonCode: "approved-excuse", revision: 1,
      recordedAtUtc: "2026-11-09T08:00:00Z"
    }], nextAfterId: null, busy: false
  };

  const node = (tag, className, value) => {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (value != null) el.textContent = String(value);
    return el;
  };
  const hasStaff = () => state.role === "Staff" || state.role === "Administrator";
  const hasAdmin = () => state.role === "Administrator";
  const isDemo = () => state.mode === "demo";
  const fmt = value => {
    const date = new Date(value);
    return Number.isNaN(date.valueOf()) ? "—" :
      date.toISOString().slice(0,16).replace("T"," ") + " UTC";
  };
  const alertText = (message, severity = "") => {
    const notice = byId("alert");
    notice.className = "notice" + (severity ? " " + severity : "");
    notice.textContent = message;
  };
  const statusPill = status => {
    const pill = node("span", "pill " + (status === "Late" ? "late" :
      status === "Excused" ? "excused" :
      status === "Unclassified" ? "unclassified" :
      status === "Invalid" ? "invalid" : ""), names[status] || "לא מסווג");
    return pill;
  };
  const showView = key => {
    if (key === "events" && !hasStaff()) return alertText("אירועים דורשים הרשאת צוות.", "error");
    if (key === "audit" && !hasAdmin()) return alertText("יומן תיקונים מחייב מנהל.", "error");
    state.view = key;
    document.querySelectorAll(".view-section").forEach(el => {
      el.hidden = el.id !== key + "-view";
    });
    document.querySelectorAll(".nav-item").forEach(el => {
      const selected = el.dataset.view === key;
      el.classList.toggle("is-active", selected);
      if (selected) el.setAttribute("aria-current", "page");
      else el.removeAttribute("aria-current");
    });
    byId("page-title").textContent = viewTitles[key][0];
    byId("breadcrumb-view").textContent = viewTitles[key][0];
    byId("page-subtitle").textContent = viewTitles[key][1];
    if (key === "audit") renderAudit();
    if (key === "events") renderEvents();
    if (key === "system") void renderSystem();
  };
  const countMap = events => {
    const counts = {};
    for (const item of events) counts[item.status] = (counts[item.status] || 0) + 1;
    return counts;
  };
  const renderOverview = () => {
    const counts = isDemo() ? countMap(state.events) : state.counts;
    const total = Object.values(counts).reduce((a,b) => a + Number(b || 0),0);
    byId("metric-total").textContent = total.toLocaleString("he-IL");
    byId("metric-on-time").textContent = Number(counts.OnTime || 0).toLocaleString("he-IL");
    byId("metric-late").textContent = Number(counts.Late || 0).toLocaleString("he-IL");
    byId("metric-unclassified").textContent = Number(counts.Unclassified || 0).toLocaleString("he-IL");
    const distribution = byId("distribution");
    distribution.replaceChildren();
    for (const status of ["OnTime","Late","Excused","Unclassified"]) {
      const count = Number(counts[status] || 0);
      const item = node("div","bar-row");
      item.append(node("span","",names[status]));
      const track = node("div","bar-track");
      const fill = node("div","bar-fill");
      fill.style.width = (total ? Math.round(count / total * 100) : 0) + "%";
      track.append(fill);
      item.append(track,node("span","bar-number",count));
      distribution.append(item);
    }
    byId("ops-edit").textContent = hasStaff() ? "זמין לתפקיד צוות" : "חסום לתפקיד צופה";
    populateTable(byId("recent-rows"), hasStaff() ? state.events.slice(0,5) : [], false);
    if (!hasStaff()) {
      const tr = node("tr");
      const td = node("td","", "נתוני אירועים דורשים הרשאת צוות.");
      td.colSpan = 4;
      tr.append(td);
      byId("recent-rows").append(tr);
    }
  };
  const populateTable = (container, records, actions) => {
    container.replaceChildren();
    records.forEach(item => {
      const tr = node("tr");
      const id = node("td");
      id.append(node("span","mono","#" + item.eventId));
      const time = node("td");
      time.append(node("span","mono",fmt(item.occurredAtUtc)));
      const status = node("td");
      status.append(statusPill(item.status));
      const revision = node("td","",String(item.revision));
      tr.append(id,time,status,revision);
      if (actions) {
        const td = node("td");
        if (hasStaff()) {
          const button = node("button","table-action","תיקון");
          button.type = "button";
          button.setAttribute("aria-label","תיקון אירוע " + item.eventId);
          button.addEventListener("click", () => openCorrection(item));
          td.append(button);
        } else td.textContent = "ללא הרשאה";
        tr.append(td);
      }
      container.append(tr);
    });
  };
  const renderEvents = () => {
    const query = byId("search-events").value.trim();
    const status = byId("filter-status").value;
    const records = state.events.filter(row =>
      (!query || String(row.eventId).includes(query)) &&
      (status === "all" || row.status === status));
    populateTable(byId("all-rows"), records, true);
    byId("events-empty").hidden = records.length > 0;
    byId("events-count").textContent = records.length + " אירועים מוצגים";
    byId("load-more").hidden = isDemo() || !hasStaff() || state.nextAfterId == null;
  };
  const renderAudit = () => {
    const container = byId("audit-list");
    container.replaceChildren();
    if (!hasAdmin()) {
      container.append(node("p","muted","יומן תיקונים מחייב הרשאת מנהל."));
      return;
    }
    if (state.audit.length === 0) {
      container.append(node("p","muted","עדיין אין תיקונים להצגה."));
      return;
    }
    for (const entry of state.audit.slice(0,50)) {
      const item = node("article","audit-item");
      const meta = node("div");
      meta.append(node("strong","", "אירוע #" + entry.eventId + " · תיקון " + entry.revision));
      meta.append(node("p","", (names[entry.beforeStatus] || entry.beforeStatus) +
        " ← " + (names[entry.afterStatus] || entry.afterStatus) +
        " · " + entry.reasonCode));
      item.append(meta,node("span","mono",fmt(entry.recordedAtUtc)));
      container.append(item);
    }
  };
  const renderPermissions = () => {
    byId("current-role").textContent = isDemo() ? "תפקיד הדגמה: מנהל" : (state.role || "לא מחובר");
    byId("connection-state").textContent = isDemo() ? "נתונים סינתטיים" :
      state.token ? "API מקומי מאומת" : "אין חיבור";
    byId("environment-text").textContent = isDemo() ? "סביבת הדגמה בלבד" : "API מקומי · בדיקות";
    byId("api-connection").hidden = isDemo();
    document.querySelectorAll(".demo-only").forEach(x => {x.hidden = !isDemo();});
    document.querySelectorAll('.nav-item[data-view="events"]').forEach(x => {x.hidden = !hasStaff();});
    document.querySelectorAll('.nav-item[data-view="audit"]').forEach(x => {x.hidden = !hasAdmin();});
    if ((state.view === "events" && !hasStaff()) || (state.view === "audit" && !hasAdmin()))
      state.view = "overview";
    showView(state.view);
    renderOverview();
    renderEvents();
  };

  const apiGet = async (path, options = {}) => {
    if (!state.token) throw new Error("נדרש JWT זמני תקף לפני קריאת API.");
    if (!["localhost","127.0.0.1","[::1]"].includes(location.hostname))
      throw new Error("חיבור API מותר במחשב המקומי בלבד.");
    const response = await fetch(path, {
      ...options, cache:"no-store", credentials:"omit", mode:"same-origin",
      headers: {Authorization:"Bearer " + state.token,
        ...(options.body ? {"Content-Type":"application/json"} : {})}
    });
    if (!response.ok) {
      if (response.status === 401) throw new Error("פג תוקף הטוקן או החתימה אינה מוכרת (401).");
      if (response.status === 403) throw new Error("אין הרשאה לפעולה זו (403).");
      if (response.status === 409) throw new Error("הרשומה השתנתה. רענן ובדוק את הגרסה (409).");
      if (response.status === 501) throw new Error("ייצוא חסום בשרת עד אישור פרטיות.");
      throw new Error("שגיאת API: " + response.status);
    }
    return response.json();
  };
  const refresh = async (loadMore = false) => {
    if (state.busy) return;
    state.busy = true;
    byId("refresh-button").disabled = true;
    try {
      if (isDemo()) {
        if (!loadMore) state.events = sample.map(x => ({...x}));
        state.counts = countMap(state.events);
        if (!loadMore) state.audit = [{
          eventId:1004,beforeStatus:"Late",afterStatus:"Excused",
          reasonCode:"approved-excuse",revision:1,recordedAtUtc:"2026-11-09T08:00:00Z"
        }];
        alertText("מצב הדגמה: הנתונים נוצרו לצורכי תצוגה בלבד.");
      } else {
        if (!state.token) {
          alertText("להצגת נתוני API יש להזין JWT קצר־מועד ממקור מורשה.", "error");
          return;
        }
        const countResponse = await apiGet("/api/v1/overview");
        state.counts = countResponse.counts || {};
        if (hasStaff()) {
          const after = loadMore ? state.nextAfterId : null;
          const response = await apiGet("/api/v1/events?limit=20" +
            (after == null ? "" : "&afterId=" + encodeURIComponent(after)));
          const newRows = Array.isArray(response.items) ? response.items : [];
          state.events = loadMore ? state.events.concat(newRows) : newRows;
          state.nextAfterId = response.nextAfterId;
        } else {
          state.events = [];
          state.nextAfterId = null;
        }
        if (hasAdmin()) {
          try {
            const response = await apiGet("/api/v1/corrections?limit=50");
            state.audit = Array.isArray(response.items) ? response.items : [];
          } catch {
            state.audit = [];
          }
        } else state.audit = [];
        alertText("נתוני API מקומי נטענו באמצעות הרשאת JWT. אין מידע אישי בתצוגה.", "success");
      }
      renderPermissions();
    } catch (error) {
      alertText(error.message || "טעינת הנתונים נכשלה.", "error");
    } finally {
      state.busy = false;
      byId("refresh-button").disabled = false;
    }
  };
  const renderSystem = async () => {
    const panel = byId("operations-panel");
    panel.replaceChildren();
    if (isDemo()) {
      panel.append(node("p","muted",
        "הדגמה סינתטית בלבד: אין מדדי קליטה חיים, מכשירים או התראות אמיתיות."));
      const rows = node("dl","ops-list");
      for (const [name,value] of [
        ["בדיקות מוכנות","לא בוצעו בדיקות חיות במצב הדגמה"],
        ["קליטת מכשירים","לא מחובר"],
        ["תור התראות","הדמיה בלבד"],
        ["שגיאות מסד נתונים","לא ידוע במצב הדגמה"]
      ]) {
        const row = node("div");
        row.append(node("dt","",name),node("dd","",value));
        rows.append(row);
      }
      panel.append(rows);
      return;
    }
    if (!hasAdmin()) {
      panel.append(node("p","muted",
        "נתוני ניטור מפורטים זמינים רק למנהל מערכת מורשה."));
      return;
    }
    if (!state.token) {
      panel.append(node("p","muted","נדרש JWT מאומת כדי לקרוא מדדים."));
      return;
    }
    panel.append(node("p","muted","טוען מדדי מוכנות..."));
    try {
      const report = await apiGet("/api/v1/ops/status");
      const ops = node("dl","ops-list");
      const values = [
        ["מוכנות SQLite",report.ready ? "תקין" : "לא מוכן"],
        ["קוד מצב",report.code || "לא זמין"],
        ["קליטת מכשירים", "לא אומת"],
        ["אירועים שנקלטו (מאז אתחול)",report.metrics?.insertedEvents ?? "—"],
        ["חבילות כפולות",report.metrics?.duplicateReplays ?? "—"],
        ["שגיאות כתיבה",report.metrics?.databaseWriteErrors ?? "—"],
        ["p95 זמן שמירה (ms)",report.metrics?.persistP95Milliseconds ?? "אין מדגם"],
        ["התראות ממתינות (הדמיה)",report.pendingDryRunNotifications ?? 0],
        ["תוצאות לא ודאיות",report.uncertainDryRunNotifications ?? 0],
        ["כשל סופי",report.deadLetterDryRunNotifications ?? 0],
        ["אזהרות",Array.isArray(report.alerts) ?
          report.alerts.join(", ") || "אין אזהרות" : "לא ידוע"]
      ];
      for (const [name,value] of values) {
        const row = node("div");
        row.append(node("dt","",name),node("dd","",String(value)));
        ops.append(row);
      }
      panel.replaceChildren(ops);
    } catch (error) {
      panel.replaceChildren(node("p","muted",
        error.message || "לא ניתן לטעון את מדדי התפעול."));
    }
  };

  const openCorrection = item => {
    if (!hasStaff()) return;
    byId("correction-event-id").value = String(item.eventId);
    byId("correction-revision").value = String(item.revision);
    byId("dialog-context").textContent = "אירוע #" + item.eventId +
      " · מצב נוכחי: " + (names[item.status] || item.status) +
      " · גרסה: " + item.revision;
    byId("correction-status").value = "";
    byId("correction-reason").value = "";
    byId("correction-dialog").showModal();
  };
  const applyCorrection = async event => {
    event.preventDefault();
    if (!hasStaff()) return;
    const id = Number(byId("correction-event-id").value);
    const expectedRevision = Number(byId("correction-revision").value);
    const newStatus = byId("correction-status").value;
    const reasonCode = byId("correction-reason").value;
    if (!["OnTime","Late","Excused","Unclassified","Invalid"].includes(newStatus) ||
        !["clock-skew","approved-excuse","duplicate-scan",
          "manual-verification","administrative-correction"].includes(reasonCode)) return;
    const item = state.events.find(x => x.eventId === id);
    if (!item || item.revision !== expectedRevision) {
      alertText("גרסת הרשומה אינה עדכנית; נדרש רענון.", "error");
      return;
    }
    try {
      if (isDemo()) {
        if (item.status === newStatus) throw new Error("אין שינוי במצב הנוכחי.");
        state.audit.unshift({eventId:id,beforeStatus:item.status,
          afterStatus:newStatus,reasonCode,revision:item.revision+1,
          recordedAtUtc:new Date().toISOString()});
        item.status = newStatus;
        item.revision++;
        alertText("תיקון סינתטי נשמר בזיכרון הדפדפן בלבד.", "success");
      } else {
        await apiGet("/api/v1/events/" + id + "/corrections", {
          method:"POST",body:JSON.stringify({expectedRevision,newStatus,reasonCode})
        });
        alertText("התיקון התקבל ב־API ונרשם לביקורת.", "success");
      }
      byId("correction-dialog").close();
      if (!isDemo()) await refresh();
      else {renderOverview();renderEvents();renderAudit();}
    } catch (error) {
      alertText(error.message || "התיקון לא נשמר.", "error");
      byId("correction-dialog").close();
    }
  };

  // CSV is available for **synthetic demo only**. No production export API.
  const csvCell = value => {
    let text = String(value);
    if (/^[\s]*[=+\-@\t\r]/.test(text)) text = "'" + text;
    return '"' + text.replaceAll('"','""') + '"';
  };
  const exportDemo = () => {
    if (!isDemo() || !hasStaff()) return;
    const cols = ["מזהה אירוע","זמן UTC","מצב","גרסה"];
    const rows = state.events.map(x => [
      x.eventId,x.occurredAtUtc,names[x.status] || x.status,x.revision
    ]);
    const csv = "\ufeff" + [cols,...rows].map(row => row.map(csvCell).join(",")).join("\r\n");
    const url = URL.createObjectURL(new Blob([csv],{type:"text/csv;charset=utf-8"}));
    const link = document.createElement("a");
    link.href = url;
    link.download = "attendance-synthetic-demo.csv";
    link.click();
    URL.revokeObjectURL(url);
    alertText("יוצא CSV עם מזהי אירועים סינתטיים בלבד; אין נתונים אמיתיים.");
  };

  document.querySelectorAll("[data-view]").forEach(button => {
    button.addEventListener("click", () => showView(button.dataset.view));
  });
  byId("mode-select").addEventListener("change", event => {
    state.mode = event.target.value;
    state.token = null; state.role = isDemo() ? "Administrator" : "";
    state.events = isDemo() ? sample.map(x => ({...x})) : [];
    state.counts = {};
    state.audit = []; state.nextAfterId = null;
    byId("bearer-token").value = "";
    state.view = "overview";
    renderPermissions();
    refresh();
  });
  byId("refresh-button").addEventListener("click", () => refresh());
  byId("search-events").addEventListener("input", renderEvents);
  byId("filter-status").addEventListener("change", renderEvents);
  byId("load-more").addEventListener("click", () => refresh(true));
  byId("demo-csv").addEventListener("click", exportDemo);
  byId("dialog-close").addEventListener("click", () => byId("correction-dialog").close());
  byId("dialog-cancel").addEventListener("click", () => byId("correction-dialog").close());
  byId("correction-form").addEventListener("submit", applyCorrection);
  byId("token-form").addEventListener("submit", async event => {
    event.preventDefault();
    if (isDemo()) return;
    state.token = byId("bearer-token").value.trim();
    byId("bearer-token").value = "";
    try {
      const identity = await apiGet("/api/v1/me");
      const roles = Array.isArray(identity.roles) ? identity.roles : [];
      state.role = ["Administrator","Staff","Viewer"].find(x => roles.includes(x)) || "";
      if (!state.role) throw new Error("JWT אינו מכיל תפקיד מורשה.");
      state.view = "overview";
      renderPermissions();
      await refresh();
    } catch (error) {
      state.token = null;state.role = "";renderPermissions();
      alertText(error.message || "אימות לא הצליח.", "error");
    }
  });
  byId("disconnect").addEventListener("click", () => {
    state.token = null;state.role = "";state.events = [];state.audit = [];
    state.counts = {};state.nextAfterId = null;
    byId("bearer-token").value = "";state.view = "overview";
    renderPermissions();alertText("החיבור נותק; הטוקן הוסר מזיכרון התצוגה.");
  });
  window.addEventListener("pagehide", () => {state.token = null;});
  renderPermissions();
  refresh();
})();