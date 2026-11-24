// Front-desk read load: customer search (70%), schedule board (20%), free slots (10%).
// Run (API from docker compose on :8080):
//   docker run --rm -i --add-host=host.docker.internal:host-gateway -e BASE_URL=http://host.docker.internal:8080 \
//     grafana/k6:1.0.0 run - < loadtests/front-desk.js
import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const FIRST = ['Ivan', 'Maria', 'Alexey', 'Olga', 'Dmitry', 'Anna', 'Sergey', 'Elena', 'Pavel', 'Irina'];
const LAST = ['Petrov', 'Sokolova', 'Smirnov', 'Kuznetsova', 'Volkov', 'Popova', 'Lebedev', 'Kozlova', 'Novikov', 'Morozova'];

export const options = {
  vus: 20,
  duration: '30s',
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:search}': ['p(95)<200'],
  },
};

export function setup() {
  const res = http.post(`${BASE}/api/v1/auth/login`,
    JSON.stringify({ email: 'admin@torque.local', password: 'ChangeMe-2026!' }),
    { headers: { 'Content-Type': 'application/json' } });
  const services = http.get(`${BASE}/api/v1/services`, { headers: { Authorization: `Bearer ${res.json('accessToken')}` } });
  return { token: res.json('accessToken'), serviceId: services.json()[0].id };
}

function day(offsetDays) {
  const d = new Date(Date.now() + offsetDays * 86400000);
  return d.toISOString().slice(0, 10);
}

export default function (data) {
  const params = (name) => ({ headers: { Authorization: `Bearer ${data.token}` }, tags: { name } });
  const r = Math.random();

  if (r < 0.7) {
    // A real customer: "<lastname> <number>" fragment, like a receptionist typing part of the name.
    const i = 1 + Math.floor(Math.random() * 100000);
    const term = `${LAST[Math.floor(i / 10) % 10]} ${i}`.toLowerCase();
    const res = http.get(`${BASE}/api/v1/customers?search=${encodeURIComponent(term)}`, params('search'));
    check(res, { 'search 200': (x) => x.status === 200 });
  } else if (r < 0.9) {
    const from = day(-300 - Math.floor(Math.random() * 300));
    const res = http.get(`${BASE}/api/v1/appointments?from=${from}T00:00:00Z&to=${from}T23:59:59Z`, params('board'));
    check(res, { 'board 200': (x) => x.status === 200 });
  } else {
    const res = http.get(`${BASE}/api/v1/availability?date=${day(3)}&serviceIds=${data.serviceId}`, params('availability'));
    check(res, { 'availability 200': (x) => x.status === 200 });
  }
}
