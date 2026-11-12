
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5140';
const CACHE_MODE = __ENV.CACHE_MODE || 'unknown';

const ENDPOINT = `${BASE_URL}/api/v1/games?page=1&pageSize=20`;

export const options = {
    stages: [
        { duration: '15s', target: 10 },
        { duration: '30s', target: 30 },
        { duration: '30s', target: 50 },
        { duration: '15s', target: 0 },
    ],
    thresholds: {
        http_req_failed: ['rate<0.01'],
        checks: ['rate>0.99'],
    },
};

export function setup() {
    const response = http.get(ENDPOINT);

    check(response, {
        'warm-up retornou HTTP 200': (r) => r.status === 200,
    });
}

export default function () {
    const response = http.get(ENDPOINT, {
        tags: {
            cache: CACHE_MODE,
        },
    });

    check(response, {
        'consulta retornou HTTP 200': (r) => r.status === 200,
    });
}
