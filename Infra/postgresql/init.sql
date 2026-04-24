CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE users (
  user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  display_name TEXT NOT NULL CHECK (char_length(display_name) >= 1),
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_users_display_name ON users(display_name);

CREATE TABLE anime (
  anime_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  provider TEXT NOT NULL CHECK (char_length(provider) >= 1),
  provider_id TEXT NOT NULL CHECK (char_length(provider_id) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  genres TEXT[] NOT NULL DEFAULT '{}',
  episodes INT CHECK (episodes IS NULL OR episodes >= 0),
  year INT CHECK (year IS NULL OR year >= 1900),
  trailer_youtube_id TEXT,
  created_at TIMESTAMP NOT NULL DEFAULT NOW(),
  UNIQUE(provider, provider_id)
);

CREATE INDEX idx_anime_title ON anime(title);
CREATE INDEX idx_anime_year ON anime(year);
CREATE INDEX idx_anime_provider_provider_id ON anime(provider, provider_id);

CREATE TABLE quests (
  quest_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT NOT NULL UNIQUE CHECK (char_length(code) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  description TEXT NOT NULL,
  requirements JSONB NOT NULL DEFAULT '{}'::jsonb,
  rewards JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_quests_code ON quests(code);

CREATE TABLE user_quests (
  user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  quest_id UUID NOT NULL REFERENCES quests(quest_id) ON DELETE CASCADE,
  status TEXT NOT NULL CHECK (status IN ('active', 'completed', 'failed')),
  progress JSONB NOT NULL DEFAULT '{}'::jsonb,
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, quest_id)
);

CREATE INDEX idx_user_quests_status ON user_quests(status);
CREATE INDEX idx_user_quests_updated_at ON user_quests(updated_at);

CREATE TABLE watch_entries (
  user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  anime_id UUID NOT NULL REFERENCES anime(anime_id) ON DELETE CASCADE,
  status TEXT NOT NULL CHECK (status IN ('watching', 'completed', 'planned', 'dropped', 'on_hold')),
  score INT CHECK (score IS NULL OR (score >= 0 AND score <= 10)),
  episodes_watched INT NOT NULL DEFAULT 0 CHECK (episodes_watched >= 0),
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, anime_id)
);

CREATE INDEX idx_watch_entries_status ON watch_entries(status);
CREATE INDEX idx_watch_entries_updated_at ON watch_entries(updated_at);

CREATE TABLE achievements (
  achievement_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT NOT NULL UNIQUE CHECK (char_length(code) >= 1),
  title TEXT NOT NULL CHECK (char_length(title) >= 1),
  description TEXT NOT NULL,
  category TEXT NOT NULL CHECK (char_length(category) >= 1),
  icon TEXT,
  requirements JSONB NOT NULL DEFAULT '{}'::jsonb,
  points INT NOT NULL DEFAULT 0 CHECK (points >= 0),
  is_hidden BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_achievements_code ON achievements(code);
CREATE INDEX idx_achievements_category ON achievements(category);

CREATE TABLE user_achievements (
  user_id UUID NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
  achievement_id UUID NOT NULL REFERENCES achievements(achievement_id) ON DELETE CASCADE,
  status TEXT NOT NULL DEFAULT 'locked' CHECK (status IN ('locked', 'in_progress', 'unlocked')),
  progress JSONB NOT NULL DEFAULT '{}'::jsonb,
  unlocked_at TIMESTAMP,
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  PRIMARY KEY (user_id, achievement_id)
);

CREATE INDEX idx_user_achievements_status ON user_achievements(status);
CREATE INDEX idx_user_achievements_unlocked_at ON user_achievements(unlocked_at);

INSERT INTO users (user_id, display_name) VALUES
  ('11111111-1111-1111-1111-111111111111', 'Akira'),
  ('22222222-2222-2222-2222-222222222222', 'Mina'),
  ('33333333-3333-3333-3333-333333333333', 'Rin')
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO anime (provider, provider_id, title, genres, episodes, year, trailer_youtube_id) VALUES
  ('anime_planet', 'ap_001', 'Fullmetal Alchemist: Brotherhood', ARRAY['Action', 'Adventure', 'Fantasy'], 64, 2009, '2uq34TeWEdQ'),
  ('anime_planet', 'ap_002', 'Attack on Titan', ARRAY['Action', 'Drama', 'Mystery'], 87, 2013, 'MGRm4IzK1SQ'),
  ('anime_planet', 'ap_003', 'Your Name', ARRAY['Drama', 'Romance', 'Supernatural'], 1, 2016, 'xU47nhruN-Q')
ON CONFLICT (provider, provider_id) DO NOTHING;

INSERT INTO quests (code, title, description, requirements, rewards) VALUES
  ('watch_5_eps', 'Warm-up Marathon', 'Watch five anime episodes this week.', '{"episodes":5}'::jsonb, '{"xp":50,"coins":100}'::jsonb),
  ('rate_3_titles', 'Critic Apprentice', 'Rate three different anime titles.', '{"ratings":3}'::jsonb, '{"xp":40,"item":"review_badge"}'::jsonb),
  ('complete_series', 'Finale Hunter', 'Complete one anime series.', '{"completed_series":1}'::jsonb, '{"xp":100,"coins":250}'::jsonb)
ON CONFLICT (code) DO NOTHING;

INSERT INTO watch_entries (user_id, anime_id, status, score, episodes_watched, updated_at)
SELECT
  '11111111-1111-1111-1111-111111111111'::uuid,
  a.anime_id,
  'watching',
  9,
  12,
  NOW()
FROM anime a
WHERE a.provider = 'anime_planet' AND a.provider_id = 'ap_001'
ON CONFLICT (user_id, anime_id) DO NOTHING;

INSERT INTO watch_entries (user_id, anime_id, status, score, episodes_watched, updated_at)
SELECT
  '22222222-2222-2222-2222-222222222222'::uuid,
  a.anime_id,
  'completed',
  8,
  87,
  NOW()
FROM anime a
WHERE a.provider = 'anime_planet' AND a.provider_id = 'ap_002'
ON CONFLICT (user_id, anime_id) DO NOTHING;

INSERT INTO user_quests (user_id, quest_id, status, progress, updated_at)
SELECT
  '11111111-1111-1111-1111-111111111111'::uuid,
  q.quest_id,
  'active',
  '{"episodes_watched":2}'::jsonb,
  NOW()
FROM quests q
WHERE q.code = 'watch_5_eps'
ON CONFLICT (user_id, quest_id) DO NOTHING;

INSERT INTO user_quests (user_id, quest_id, status, progress, updated_at)
SELECT
  '22222222-2222-2222-2222-222222222222'::uuid,
  q.quest_id,
  'completed',
  '{"ratings":3}'::jsonb,
  NOW()
FROM quests q
WHERE q.code = 'rate_3_titles'
ON CONFLICT (user_id, quest_id) DO NOTHING;

INSERT INTO achievements (code, title, description, category, icon, requirements, points, is_hidden) VALUES
  ('first_login', 'Welcome, Adventurer', 'Log in for the first time.', 'profile', 'spark', '{"logins":1}'::jsonb, 10, FALSE),
  ('quest_runner', 'Quest Runner', 'Complete your first quest.', 'quests', 'scroll', '{"completed_quests":1}'::jsonb, 25, FALSE),
  ('anime_marathon', 'Anime Marathon', 'Watch 50 episodes total.', 'watching', 'clapperboard', '{"episodes_watched":50}'::jsonb, 50, FALSE)
ON CONFLICT (code) DO NOTHING;

INSERT INTO user_achievements (user_id, achievement_id, status, progress, unlocked_at, updated_at)
SELECT
  '11111111-1111-1111-1111-111111111111'::uuid,
  a.achievement_id,
  'unlocked',
  '{"logins":1}'::jsonb,
  NOW(),
  NOW()
FROM achievements a
WHERE a.code = 'first_login'
ON CONFLICT (user_id, achievement_id) DO NOTHING;

INSERT INTO user_achievements (user_id, achievement_id, status, progress, unlocked_at, updated_at)
SELECT
  '22222222-2222-2222-2222-222222222222'::uuid,
  a.achievement_id,
  'in_progress',
  '{"episodes_watched":34}'::jsonb,
  NULL,
  NOW()
FROM achievements a
WHERE a.code = 'anime_marathon'
ON CONFLICT (user_id, achievement_id) DO NOTHING;
