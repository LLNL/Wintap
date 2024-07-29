const PROXY_CONFIG = [
    {
        context: [
            "/api/",
        ],
        target: "http://127.0.0.1:5000",
        secure: false,
        changeOrigin: true,
    },
    {
        context: ["/signalr/"],
        target: "http://127.0.0.1:5000",
        secure: false,
        changeOrigin: true,
        ws: true, 
        timeout: 10000, 
        logLevel: "debug",
    }
]

module.exports = PROXY_CONFIG;
