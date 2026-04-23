import { check } from 'k6';
import http from 'k6/http';

function parseBoolean(value) {
  return ['1', 'true', 'yes', 'y'].includes(String(value || '').toLowerCase());
}

const targetVus = Number.parseInt(__ENV.VUS || '25', 10);
const holdDuration = __ENV.DURATION || '5m';
const skipTlsVerify = parseBoolean(__ENV.SKIP_TLS_VERIFY);
const baseUrl = String(__ENV.BASE_URL || '').replace(/\/+$/, '');
const authorId = __ENV.AUTHOR_ID;
const bookId = __ENV.BOOK_ID;
const bookLimitEndpoints = [
  { endpoint: 'books_limit_10', path: '/api/v1/books?limit=10' },
  { endpoint: 'books_limit_100', path: '/api/v1/books?limit=100' },
  { endpoint: 'books_limit_1000', path: '/api/v1/books?limit=1000' },
  { endpoint: 'books_limit_10000_default', path: '/api/v1/books' },
  // { endpoint: 'books_limit_50000', path: '/api/v1/books?limit=50000' },
  // { endpoint: 'books_limit_100000', path: '/api/v1/books?limit=100000' },
];

if (!baseUrl) {
  throw new Error('BASE_URL is required. Example: BASE_URL=https://<service-public-ip-or-dns>');
}

const thresholds = {
  http_req_failed: ['rate<0.01'],
  'http_req_duration{endpoint:health}': ['p(95)<300', 'p(99)<750'],
  'http_req_duration{endpoint:authors}': ['p(95)<1000', 'p(99)<2500'],
  'http_req_duration{endpoint:books_limit_10}': ['p(95)<1000', 'p(99)<2500'],
  'http_req_duration{endpoint:books_limit_100}': ['p(95)<1500', 'p(99)<3000'],
  'http_req_duration{endpoint:books_limit_1000}': ['p(95)<3000', 'p(99)<6000'],
  'http_req_duration{endpoint:books_limit_10000_default}': ['p(95)<10000', 'p(99)<20000'],
  // 'http_req_duration{endpoint:books_limit_50000}': ['p(95)<30000', 'p(99)<60000'],
  // 'http_req_duration{endpoint:books_limit_100000}': ['p(95)<60000', 'p(99)<120000'],
};

if (authorId) {
  thresholds['http_req_duration{endpoint:books_by_author}'] = ['p(95)<10000', 'p(99)<20000'];
}

if (bookId) {
  thresholds['http_req_duration{endpoint:book_by_id}'] = ['p(95)<1000', 'p(99)<2500'];
}

export const options = {
  insecureSkipTLSVerify: skipTlsVerify,
  stages: [
    { duration: '30s', target: Number.isFinite(targetVus) && targetVus > 0 ? targetVus : 25 },
    { duration: holdDuration, target: Number.isFinite(targetVus) && targetVus > 0 ? targetVus : 25 },
    { duration: '30s', target: 0 },
  ],
  thresholds,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function get(endpoint, path) {
  const response = http.get(`${baseUrl}${path}`, {
    tags: {
      endpoint,
    },
  });

  check(response, {
    [`${endpoint} returned HTTP 200`]: (res) => res.status === 200,
  });
}

export default function () {
  get('health', '/health');
  get('authors', '/api/v1/authors');

  for (const limitEndpoint of bookLimitEndpoints) {
    get(limitEndpoint.endpoint, limitEndpoint.path);
  }

  if (authorId) {
    get('books_by_author', `/api/v1/books?author_id=${encodeURIComponent(authorId)}`);
  }

  if (bookId) {
    get('book_by_id', `/api/v1/books/${encodeURIComponent(bookId)}`);
  }
}
