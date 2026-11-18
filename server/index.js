const express = require('express');
const http = require('http');
const { Server } = require('socket.io');
const cors = require('cors');
const { v4: uuidv4 } = require('uuid');

const app = express();
app.use(cors());
app.use(express.json());

const server = http.createServer(app);
const io = new Server(server, {
  cors: {
    origin: '*',
    methods: ['GET', 'POST']
  }
});

// sessions: { sessionId: { hostSocketId, viewerSocketIds[] } }
const sessions = new Map();

app.get('/health', (req, res) => {
  res.json({ status: 'ok', sessions: sessions.size });
});

app.post('/session/create', (req, res) => {
  const sessionId = uuidv4().split('-')[0].toUpperCase();
  sessions.set(sessionId, { hostSocketId: null, viewerSocketIds: [] });
  res.json({ sessionId });
});

io.on('connection', (socket) => {
  console.log('Client connected:', socket.id);

  // Host (C# exe) joins as streamer
  socket.on('host-join', ({ sessionId }) => {
    const session = sessions.get(sessionId);
    if (!session) {
      socket.emit('error', { message: 'Session not found' });
      return;
    }
    session.hostSocketId = socket.id;
    socket.join(sessionId);
    socket.sessionId = sessionId;
    socket.role = 'host';
    console.log(`Host joined session: ${sessionId}`);
    socket.emit('host-joined', { sessionId });
  });

  // Viewer (browser) joins to watch
  socket.on('viewer-join', ({ sessionId }) => {
    const session = sessions.get(sessionId);
    if (!session) {
      socket.emit('error', { message: 'Session not found' });
      return;
    }
    session.viewerSocketIds.push(socket.id);
    socket.join(sessionId);
    socket.sessionId = sessionId;
    socket.role = 'viewer';
    console.log(`Viewer joined session: ${sessionId}`);

    // Notify host that a viewer connected, so it starts WebRTC offer
    if (session.hostSocketId) {
      io.to(session.hostSocketId).emit('viewer-connected', { viewerId: socket.id });
    }
    socket.emit('viewer-joined', { sessionId });
  });

  // WebRTC signaling relay
  socket.on('webrtc-offer', ({ targetId, offer }) => {
    io.to(targetId).emit('webrtc-offer', { senderId: socket.id, offer });
  });

  socket.on('webrtc-answer', ({ targetId, answer }) => {
    io.to(targetId).emit('webrtc-answer', { senderId: socket.id, answer });
  });

  socket.on('webrtc-ice-candidate', ({ targetId, candidate }) => {
    io.to(targetId).emit('webrtc-ice-candidate', { senderId: socket.id, candidate });
  });

  socket.on('disconnect', () => {
    console.log('Client disconnected:', socket.id);
    if (!socket.sessionId) return;

    const session = sessions.get(socket.sessionId);
    if (!session) return;

    if (socket.role === 'host') {
      session.hostSocketId = null;
      io.to(socket.sessionId).emit('host-disconnected');
    } else if (socket.role === 'viewer') {
      session.viewerSocketIds = session.viewerSocketIds.filter(id => id !== socket.id);
      if (session.hostSocketId) {
        io.to(session.hostSocketId).emit('viewer-disconnected', { viewerId: socket.id });
      }
    }
  });
});

const PORT = process.env.PORT || 3000;
server.listen(PORT, () => {
  console.log(`Signaling server running on port ${PORT}`);
});
