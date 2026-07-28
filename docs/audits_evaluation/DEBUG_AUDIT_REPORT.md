# DEBUG AUDIT REPORT

Ngày thực hiện: 2026-07-28
Phạm vi: toàn bộ workspace `D:\KLTN` (frontend, backend, AI service, configuration, tests, scripts, schema bootstrap và tài liệu).
Baseline: worktree hiện tại đã có thay đổi P0 chưa commit trước khi audit này bắt đầu. Báo cáo không coi các thay đổi có sẵn đó là kết quả riêng của đợt debug.

## 1. Executive Summary

- Phát hiện 17 lỗi có bằng chứng kỹ thuật.
- Đã sửa 17/17 lỗi được tái hiện hoặc chứng minh trực tiếp.
- Không còn lỗi Critical đã biết chưa được ghi nhận.
- Lỗi nghiêm trọng nhất:
  - cả backend và AI service không thể khởi động vì shared internal key chỉ dài 28 ký tự trong khi code yêu cầu tối thiểu 32 byte;
  - schema bootstrap tự động xóa các evaluation trùng và requirement match liên quan mỗi lần khởi động.
- Final build:
  - backend clean/build: đạt, 0 warning, 0 error;
  - frontend TypeScript/Vite build: đạt;
  - Python compile: đạt.
- Final tests:
  - AI service: 47 passed, 9 dependency deprecation warnings;
  - frontend contract: 1 passed;
  - backend regression console suite: 4 passed;
  - integration harness: build đạt nhưng runtime bị chặn an toàn vì cấu hình đang trỏ đến database Neon tên `neondb`, không phải test database;
  - E2E: không chạy vì không có PostgreSQL test cô lập và không được phép ghi vào database ngoài.
- AI service và frontend đã được khởi động cục bộ, đều trả HTTP 200.
- Backend vượt qua validation cấu hình sau khi xoay key; startup đầy đủ dừng tại kết nối PostgreSQL test cục bộ không tồn tại. Không có thao tác ghi nào vào Neon.

## 2. Project Architecture

### Frontend stack

- Vite 8, TypeScript 6, vanilla DOM rendering.
- Entry point: `frontend/src/main.ts`.
- UI templates: `frontend/src/views.ts`.
- REST client: `frontend/src/api.ts`.
- State được giữ trong một `AppState` tại client; JWT được lưu trong `localStorage`.
- Chart.js phục vụ dashboard admin.

### Backend stack

- ASP.NET Core 9 Web API.
- Entry point và middleware pipeline: `backend/ReqSimulator.API/Program.cs`.
- Controllers: Auth, Scenarios, Sessions, Admin, AdminScenarios.
- EF Core 9 + Npgsql.
- Schema bootstrap và seed chạy khi application startup.
- JWT bearer authentication, role authorization `Student`, `Lecturer`, `Admin`.
- Global exception handler, CORS, security headers và fixed-window rate limiting.

### AI service

- FastAPI + Pydantic.
- Entry point: `ai-service/app/main.py`.
- Các endpoint nội bộ `/api/chat`, `/api/extract`, `/api/evaluate`, crawler và video ingestion.
- Backend gọi AI service bằng header `X-AI-Service-Key`.
- Provider: Gemini, Groq, DeepSeek, Mimo và OpenRouter.

### Database

PostgreSQL với graph chính:

`User -> Scenario/Persona/HiddenRequirement -> SimulationSession -> Message -> ExtractedRequirement/EvaluationResult -> RequirementMatch`

Các thành phần bổ sung: scenario version metadata, finalization lease, lecturer override audit.

### Authentication và authorization

1. Frontend gửi register/login đến backend.
2. Backend xác thực BCrypt; hash SHA-256 legacy được xác thực rồi nâng cấp sang BCrypt.
3. Backend phát JWT chứa user id, email và role.
4. Frontend gửi `Authorization: Bearer <token>`.
5. Backend áp dụng authentication, global rate limiter, sau đó authorization.
6. AI service không public trực tiếp với người dùng; mọi `/api/*` yêu cầu shared internal key, trừ health endpoint.

### External services

- PostgreSQL/Neon.
- Gemini API.
- Groq API.
- DeepSeek API.
- Mimo API.
- OpenRouter API.
- HTTP(S) website crawler.
- FFmpeg cho media extraction.

### Luồng dữ liệu chính

1. Frontend tải scenario và tạo session.
2. Student gửi message.
3. Backend tải snapshot session, persona, history và hidden requirements.
4. Backend gọi AI chat ngoài transaction.
5. Backend khóa row session, kiểm tra history chưa thay đổi, rồi ghi message/state trong transaction ngắn.
6. Khi kết thúc, backend claim finalization lease, gọi extract/evaluate, ghi evaluation và matches.
7. Lecturer/Admin đọc report và có thể override match type; backend tính lại coverage và lưu audit.

### Biến môi trường

Các biến chính được đọc từ `.env.example`:

- Backend: `ConnectionStrings__DefaultConnection`, `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiresInHours`, `AiService__BaseUrl`, `AiService__InternalKey`, rate-limit settings, `SeedData__Enabled`, `BootstrapUsers__Enabled`.
- AI: `AI_SERVICE_INTERNAL_KEY`, `MODEL_NAME`, `EMBEDDING_MODEL`, provider API keys, matching policy.
- Frontend: `VITE_API_BASE_URL`.

Không có secret nào được ghi vào báo cáo.

### Lệnh dự án

| Mục đích | Lệnh |
| --- | --- |
| Frontend install | `npm install` hoặc `npm ci` |
| Frontend dev | `npm run dev` |
| Frontend build/type check | `npm run build` |
| Frontend tests | `npm test` |
| Backend restore | `dotnet restore backend/ReqSimulator.API/ReqSimulator.API.csproj` |
| Backend run | `dotnet run --project backend/ReqSimulator.API/ReqSimulator.API.csproj` |
| Backend build | `dotnet build backend/ReqSimulator.API/ReqSimulator.API.csproj` |
| Backend integration build/run | `dotnet build/run --project backend/ReqSimulator.API.IntegrationTests/...` |
| AI install | `.venv\Scripts\python.exe -m pip install -r requirements.txt` |
| AI run | `.venv\Scripts\python.exe -m uvicorn app.main:app` |
| AI tests | `.venv\Scripts\python.exe -m pytest tests -q` |

Không có lint script/config cho frontend; Ruff, Pyright và ESLint không được cài. TypeScript checking chạy trong frontend build.

## 3. Initial Baseline

| Command | Result | Errors | Warnings |
| --- | --- | --- | --- |
| `npm ci --ignore-scripts` | Fail, exit 1 | `EPERM` khi unlink existing pnpm-style `node_modules` | Không |
| `npm install --ignore-scripts` | Pass | Không | 18 packages được thêm |
| `dotnet restore` API + integration | Pass | Không | Không |
| Python requirements install | Pass | Không | Packages đã thỏa |
| Backend build | Pass | Không | 0 |
| Frontend build | Pass | Không | 0 |
| Python compileall | Pass | Không | 0 |
| AI pytest ban đầu | Pass, 40 tests | Không | 9 dependency deprecation warnings |
| Integration build | Pass | Không | 0 |
| Integration runtime | Blocked | Harness từ chối database Neon `neondb` | Đây là guard an toàn |
| AI startup | Fail | `AI_SERVICE_INTERNAL_KEY` phải dài tối thiểu 32 | Không |
| Backend startup | Fail | `AiService:InternalKey` phải dài tối thiểu 32, `Program.cs` | Không |
| Frontend startup | Pass | HTTP 200 | Không |

## 4. Bugs Found

| ID | Severity | Bug | Root cause | Affected files | Reproduction | Status |
| --- | --- | --- | --- | --- | --- | --- |
| BUG-001 | Critical | Backend và AI không khởi động | Hai `.env` chứa shared key dài 28 trong khi code yêu cầu >=32 byte | ignored `.env`, `Program.cs`, `main.py` | Start hai service, đều throw trước khi listen | Fixed |
| BUG-002 | Critical | Startup có thể xóa evaluation data | Bootstrap SQL chạy `DELETE` để dọn duplicate trước unique constraint | `SchemaBootstrapper.cs` | Source SQL + regression reflection test | Fixed |
| BUG-003 | High | Lecturer override luôn bind sai | Frontend gửi `overrides/overriddenType`, backend yêu cầu `matchOverrides/newMatchType` | `main.ts`, `contracts.ts`, `SessionsController.cs` | .NET JSON binding cho `MatchOverrides == null` | Fixed |
| BUG-004 | High | Login tài khoản SHA-256 legacy trả 500 | Gọi BCrypt trước nhánh nhận diện 64-char SHA; BCrypt throw `SaltParseException` | `AuthService.cs` | Repro .NET với SHA-256 hash | Fixed |
| BUG-005 | High | Crawler cho phép SSRF vào loopback/private network | `AnyHttpUrl` chỉ kiểm tra syntax; redirect được follow không kiểm soát | crawler/admin service | Pydantic chấp nhận `127.0.0.1` trước fix | Fixed |
| BUG-006 | High | LLM scenario key có thể path traversal | `scenario_key` không có pattern, được ghép trực tiếp thành path | `admin_crawler_service.py` | `../../outside` được schema chấp nhận trước fix | Fixed |
| BUG-007 | High | Media extraction ghi đè file `.mp3` cạnh video | Output dùng `video_path.with_suffix(".mp3")`, unlink trước/cleanup sau | `video_processing_service.py` | Test marker file bị mất trước fix | Fixed |
| BUG-008 | High | Provider non-Gemini bỏ qua structured-output config | Groq/DeepSeek/Mimo/OpenRouter không nhận system prompt, JSON mode, token/temperature từ `GenerateContentConfig` | `api_client_manager.py` | Mock call thiếu kwargs | Fixed |
| BUG-009 | Medium | Tất cả Gemini key bị block vẫn chọn key block | Active-index helper trả index 0 khi không có key khả dụng | `api_client_manager.py` | Mock blocked clients | Fixed |
| BUG-010 | High | Hai message đồng thời có thể ghi state stale | AI được gọi từ cùng history; row lock chỉ có lúc ghi, không kiểm tra snapshot | `SessionsController.cs` | Data-flow và locking trace | Fixed |
| BUG-011 | High | Lecturer/Admin có thể mutate/end session của student | Mutation endpoints dùng privileged read access thay cho owner check | `SessionsController.cs` | Authorization branch source trace | Fixed |
| BUG-012 | Medium | AI trả HTTP 200 body `null` gây NullReference về sau | `ReadFromJsonAsync<T>()!` không kiểm tra null | `AiServiceClient.cs` | Fake HTTP handler trả JSON `null` | Fixed |
| BUG-013 | Medium | UI cho password 6 ký tự nhưng backend yêu cầu 12 | HTML validation contract cũ | `views.ts` | So sánh `minlength` và DataAnnotation | Fixed |
| BUG-014 | Medium | Request bị authorization từ chối né global rate limit | `UseAuthorization` đứng trước `UseRateLimiter` | `Program.cs` | Middleware ordering trace | Fixed |
| BUG-015 | Medium | Video endpoint cho chọn model không hỗ trợ media | Backend catalog cho mọi provider, implementation gọi Gemini file API | model catalog, controller, video service | Source contract trace | Fixed |
| BUG-016 | High | Local AI fallback bị ghi như kết quả thật | Python response không có `isFallback`; backend mặc định false | schemas, chat/extract services | Provider mock failure, response flag false trước fix | Fixed |
| BUG-017 | Low | README tạo double `/api/api/...` | Example base URL đã chứa `/api`, frontend path cũng chứa `/api` | `README.md` | Static URL composition | Fixed |

## 5. Fixes Implemented

### BUG-001: Shared internal key không hợp lệ

- Severity: Critical.
- Symptoms: cả hai process fail startup.
- Root cause: cấu hình runtime lệch validation mới.
- Evidence: key length 28 ở cả hai file; stack trace tại startup.
- Fix: tạo một random 32-byte base64url key dùng chung; không in secret ra log.
- Verification: AI `/health` HTTP 200; backend vượt validation và chỉ dừng ở PostgreSQL local test không tồn tại.
- Regression risk: cần phân phối cùng key cho mọi môi trường deploy.

### BUG-002: Bootstrap xóa dữ liệu

- Severity: Critical.
- Root cause: migration bootstrap tự quyết định giữ evaluation sớm nhất rồi xóa phần còn lại.
- Fix: bỏ toàn bộ `DELETE`; nếu có duplicate thì `RAISE EXCEPTION` rõ ràng trước khi thêm unique constraint.
- Test: reflection test khẳng định SQL không chứa `DELETE FROM` và có `RAISE EXCEPTION`.
- Regression risk: deployment có dữ liệu trùng sẽ fail-fast và cần reconciliation thủ công, nhưng không mất dữ liệu.

### BUG-003: Override DTO mismatch

- Severity: High.
- Root cause: hai phía dùng tên object/property khác nhau.
- Fix: helper typed `buildLecturerOverridePayload`.
- Test: Node contract test deep-equal payload chính xác.

### BUG-004: Legacy password

- Severity: High.
- Root cause: thứ tự verify sai.
- Fix: nhận diện SHA-256 hex trước, constant comparison hiện có, nâng cấp BCrypt sau login thành công; malformed BCrypt trả unauthorized thay vì 500.
- Test: hash SHA-256 thực được verify và đánh dấu upgrade.

### BUG-005 và BUG-006: SSRF/path traversal

- Severity: High.
- Fix:
  - chỉ HTTP(S);
  - resolve DNS và từ chối mọi IP không global;
  - kiểm tra từng redirect, tối đa 6 hop;
  - giới hạn response 2 MiB;
  - scenario key chỉ nhận lowercase snake_case;
  - resolved output phải nằm trực tiếp trong scenario directory;
  - clear scenario config cache sau save.
- Tests: loopback URL và `../../outside` đều bị validation từ chối.

### BUG-007 và BUG-015: Media handling

- Severity: High/Medium.
- Fix:
  - audio output là unique temp file, không dùng tên cạnh input;
  - cleanup chỉ xóa temp file do process tạo;
  - allowlist extension;
  - chỉ Gemini model được dùng cho video;
  - khi toàn bộ Gemini key block thì fail rõ ràng.
- Test: adjacent `.mp3` marker giữ nguyên.

### BUG-008 và BUG-009: Provider routing

- Severity: High/Medium.
- Fix: truyền system instruction, JSON response mode, temperature và max tokens đến mọi provider/fallback; không gọi client đang block.
- Tests: mock Groq kiểm tra toàn bộ kwargs; blocked Gemini clients không được gọi.

### BUG-010 và BUG-011: Session concurrency/authorization

- Severity: High.
- Fix:
  - message/end là owner-only;
  - giữ network call ngoài transaction;
  - sau `FOR UPDATE`, đếm lại message; snapshot thay đổi trả HTTP 409 và không ghi stale response.
- Verification: backend build; integration test source cover unauthorized/forbidden paths. Runtime integration cần DB test.
- Regression risk: client cần reload/retry khi nhận 409.

### BUG-012 và BUG-016: AI failure contract

- Severity: Medium/High.
- Fix: null payload và local heuristic recovery đều đặt `IsFallback`; session endpoints trả 503 thay vì ghi dữ liệu AI giả.
- Tests: 2 backend null-payload tests + 2 Python provider-failure tests.

### BUG-013, BUG-014 và BUG-017

- Password UI đồng bộ 12–128 ký tự với backend; login vẫn cho legacy password bất kỳ độ dài.
- Global rate limiter chạy sau authentication nhưng trước authorization, nên có user partition và vẫn áp dụng cho request bị deny.
- README base URL bỏ `/api`.

## 6. Frontend–Backend Contract

| Frontend call | Backend endpoint | Request schema | Response schema | Status |
| --- | --- | --- | --- | --- |
| Register | `POST /api/Auth/register` | `{name,email,password}` | success message | Matched |
| Login | `POST /api/Auth/login` | `{email,password}` | `{token}` | Matched |
| Scenarios | `GET /api/Scenarios` | none | scenario summaries | Matched |
| Scenario detail | `GET /api/Scenarios/{id}` | route Guid | scenario + personas | Matched |
| Create session | `POST /api/Sessions` | `{scenarioId,personaId,selectedModel}` | session state | Matched |
| Send message | `POST /api/Sessions/{id}/messages` | `{content}` | `{reply,questionType,stateUpdate}` | Matched; may return 409/503 |
| End session | `POST /api/Sessions/{id}/end` | none | evaluation | Matched |
| Review list/detail | `GET /api/Sessions/review[/{id}]` | route/query | review DTOs | Matched |
| Lecturer override | `PUT /api/Sessions/review/{id}/override` | `{matchOverrides:[{matchId,newMatchType}],comment}` | evaluation | Fixed and tested |
| Admin stats | `GET /api/Admin/stats/*` | query where applicable | dashboard DTOs | Matched |
| Admin users | `GET/POST/PUT/DELETE /api/Admin/users...` | user DTOs | user/result DTOs | Matched |
| Crawl | `POST /api/AdminScenarios/crawl` | `{url,selectedModel}` | publish result | Matched |
| Video | `POST /api/AdminScenarios/upload-video` | `{videoPath,selectedModel}` | publish result | Matched; Gemini only |

## 7. Files Changed

Đây là các file trực tiếp liên quan tới fixes của audit; worktree còn có các thay đổi P0 có sẵn từ trước.

| File | Change | Reason |
| --- | --- | --- |
| `ai-service/app/models/schemas.py` | Thêm fallback flags | Đồng bộ failure contract |
| `ai-service/app/services/admin_crawler_service.py` | SSRF, bounds, path validation, cache clear | Security/data integrity |
| `ai-service/app/services/admin_service.py` | URL validation | Chặn literal private IP sớm |
| `ai-service/app/services/api_client_manager.py` | Config propagation, blocked-key behavior | Provider correctness |
| `ai-service/app/services/chat_service.py` | Mark fallback | Không ghi fake response |
| `ai-service/app/services/extract_service.py` | Mark fallback | Không ghi heuristic như AI success |
| `ai-service/app/services/video_processing_service.py` | Safe temp media, model/file validation | Chặn overwrite và invalid provider |
| `backend/ReqSimulator.API/Services/AuthService.cs` | Legacy verify order | Chặn login 500 |
| `backend/ReqSimulator.API/Services/AiServiceClient.cs` | Null payload checks, structured logs | Chặn downstream NRE |
| `backend/ReqSimulator.API/Data/SchemaBootstrapper.cs` | Non-destructive unique enforcement | Bảo toàn dữ liệu |
| `backend/ReqSimulator.API/Controllers/SessionsController.cs` | Owner-only mutation, stale snapshot guard | Authorization/concurrency |
| `backend/ReqSimulator.API/Controllers/AdminScenariosController.cs` | Gemini-only video | Contract correctness |
| `backend/ReqSimulator.API/Services/AiModelCatalog.cs` | Gemini predicate | Shared validation |
| `backend/ReqSimulator.API/Program.cs` | Middleware order | Global rate limit coverage |
| `frontend/src/contracts.ts` | Override mapper | Typed FE/BE contract |
| `frontend/src/main.ts` | Use correct override payload | Fix binding |
| `frontend/src/views.ts` | Password constraints | Match backend |
| `frontend/package.json` | Test command | Repeatable regression |
| `README.md` | Correct API base URL | Correct setup |
| ignored backend/AI `.env` | Shared key rotation | Restore startup |

## 8. Tests Added

| Test | Purpose | Result |
| --- | --- | --- |
| `test_debug_regressions.py` SSRF | Reject loopback | Pass |
| Scenario key traversal | Reject `../../outside` | Pass |
| Provider config propagation | Preserve structured-output settings | Pass |
| Blocked Gemini keys | Skip blocked clients | Pass |
| Media overwrite | Preserve adjacent user file | Pass |
| `test_fallback_contracts.py` chat | Flag local recovery | Pass |
| `test_fallback_contracts.py` extract | Flag regex recovery | Pass |
| Frontend contract test | Match override DTO | Pass |
| Backend null chat/extract | Handle JSON `null` | Pass |
| Backend legacy SHA | Verify + request BCrypt upgrade | Pass |
| Backend schema SQL | No destructive duplicate cleanup | Pass |

## 9. Verification Results

| Command | Result | Notes |
| --- | --- | --- |
| `dotnet clean ...` | Pass | Clean target, 0 warning/error |
| `dotnet build ... --no-restore` | Pass | Backend 0 warning/error |
| Backend regression console suite | Pass | 4/4 |
| Integration project build | Pass | 0 warning/error |
| Integration runtime | Blocked | Refused production-like Neon DB |
| `npm test` | Pass | 1/1 |
| `npm run build` | Pass | TypeScript + Vite |
| Python `compileall` | Pass | No syntax errors |
| Python `pytest -q` | Pass | 47/47; 9 dependency deprecations |
| AI `/health` | Pass | HTTP 200 |
| Frontend dev root | Pass | HTTP 200, app root present |
| Backend startup with fake local DB | Expected DB failure | Key validation passed; no external DB touched |
| `git diff --check` | Pass | No whitespace errors |
| Empty-catch scan | Pass | No `catch {}` remaining |

## 10. Remaining Issues

| ID | Severity | Issue | Reason not fixed | Recommendation |
| --- | --- | --- | --- | --- |
| REM-001 | High verification gap | Integration/E2E chưa chạy runtime | Không có PostgreSQL test cô lập; current config là Neon `neondb` | Provision disposable local/test DB rồi chạy 13 integration scenarios và browser E2E |
| REM-002 | High security design | Video API nhận server-side path | Sửa đúng cần chuyển contract sang authenticated multipart upload/quarantine storage | Thiết kế upload API, size/MIME magic validation, AV scan, storage root |
| REM-003 | Medium | Crawler không render SPA JavaScript | HTTP crawler chỉ đọc response HTML | Dùng browser worker sandbox riêng, timeout và egress policy |
| REM-004 | Medium security | JWT ở `localStorage` | Thay đổi sang HttpOnly cookie ảnh hưởng auth contract và CSRF design | Lập migration riêng sang Secure/SameSite HttpOnly cookie |
| REM-005 | Medium | Test coverage frontend/backend còn hẹp | Frontend mới có contract test; backend unit harness tập trung regression | Bổ sung controller/service unit tests và Playwright E2E |
| REM-006 | Low | 9 dependency deprecation warnings | Đến từ Google GenAI/FastAPI/Starlette với Python 3.14 | Theo dõi minor updates; không nâng major trong audit |
| REM-007 | Low | Không có lint toolchain | ESLint/Ruff/Pyright không được cài/configure | Thêm scripts pinned và CI checks |

### Git history

- Override mismatch xuất hiện từ commit `c86bff0` (`feat: add lecturer grade override...`).
- Legacy BCrypt-before-SHA tồn tại từ initial commit `3c5530e`.
- Destructive duplicate cleanup liên quan commit `3fa4a14`.

## 11. Manual Verification Required

- Full register/login/logout/session persistence qua browser với PostgreSQL test.
- Concurrent double-submit thực trên hai HTTP clients và xác nhận request thứ hai nhận 409.
- Duplicate end-session/finalization lease trên PostgreSQL.
- Lecturer/Admin review và role authorization end-to-end.
- External provider calls bằng credentials thật.
- Video upload redesign và FFmpeg với media thật.
- SPA crawling.
- Production database duplicate reconciliation trước unique constraint.
- Cloud deployment, CORS, HTTPS/HSTS và reverse proxy IP forwarding.
- Browser-specific console/network inspection.

## 12. Final Assessment

| Tiêu chí | Trước | Sau | Bằng chứng |
| --- | ---: | ---: | --- |
| Build stability | 5/10 | 8/10 | Trước: backend/AI không start do key; sau: clean builds và AI/frontend runtime pass |
| Runtime stability | 5/10 | 7/10 | Null AI payload, blocked key, fallback contract và media overwrite đã sửa; DB E2E chưa chạy |
| Error handling | 5/10 | 8/10 | Null response có explicit fallback; không còn empty catch; error payload không log body |
| Test coverage | 4/10 | 6/10 | Thêm 12 regression assertions; vẫn thiếu broad FE/controller E2E |
| Maintainability | 6/10 | 8/10 | Shared model catalog/contract mapper; fixes nhỏ và có tests |
| Data integrity | 4/10 | 8/10 | Bỏ destructive bootstrap, stale chat guard, owner-only mutation, fallback không ghi DB |
| FE–BE consistency | 5/10 | 8/10 | Override/password/base URL đã đồng bộ và contract test pass |

Đánh giá tổng thể: hệ thống đã chuyển từ trạng thái có hai blocker khởi động và nhiều lỗi dữ liệu/contract mức High sang trạng thái build ổn định với regression tests rõ ràng. Chưa thể kết luận “đã sửa hoàn toàn” cho runtime toàn hệ thống cho đến khi có PostgreSQL test cô lập và chạy integration/E2E đầy đủ.
